using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ToolExecutionService
{
    private readonly AgentToolCatalog _toolCatalog;

    public ToolExecutionService(AgentToolCatalog toolCatalog)
    {
        _toolCatalog = toolCatalog;
    }

    public async IAsyncEnumerable<ToolExecutionEvent> ExecuteAsync(
        ToolExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tool = _toolCatalog.Find(request.ToolCall.Name);
        if (tool is null)
        {
            yield return Result(
                request.ToolCall,
                new AgentToolResult
                {
                    ToolName = request.ToolCall.Name,
                    Content = $"未知工具：{request.ToolCall.Name}",
                    IsError = true,
                    Status = ToolExecutionStatus.UnknownTool,
                    FailureReason = "Tool was not found in the enabled catalog."
                });
            yield break;
        }

        var context = new AgentToolContext
        {
            ProjectPath = request.ProjectPath,
            InputArtifacts = request.InputArtifacts
        };
        var previewResult = await TryPreviewAsync(tool, request, context, cancellationToken);
        if (previewResult.ErrorResult is not null)
        {
            yield return Result(request.ToolCall, previewResult.ErrorResult);
            yield break;
        }

        var preview = previewResult.Preview!;
        var mode = ResolvePermissionMode(request, tool);
        if (mode == ToolPermissionMode.Disabled)
        {
            yield return Result(
                request.ToolCall,
                new AgentToolResult
                {
                    ToolName = request.ToolCall.Name,
                    IsError = true,
                    Content = "该工具已在设置中关闭，未执行。",
                    Status = ToolExecutionStatus.Disabled,
                    FailureReason = "Tool is disabled by permission settings."
                },
                preview);
            yield break;
        }

        if (!IsAutoApproved(request, tool, mode))
        {
            yield return new ToolExecutionEvent
            {
                Type = ToolExecutionEventType.ApprovalRequired,
                ToolCall = request.ToolCall,
                Preview = preview
            };

            var approval = request.RequestToolApprovalAsync is null
                ? ToolApprovalDecision.Reject("工具需要确认，但当前界面没有提供确认处理器。")
                : await request.RequestToolApprovalAsync(
                    new ToolApprovalRequest { ToolCall = request.ToolCall, Preview = preview },
                    cancellationToken);

            if (!approval.IsApproved)
            {
                yield return new ToolExecutionEvent
                {
                    Type = ToolExecutionEventType.ApprovalRejected,
                    ToolCall = request.ToolCall,
                    Preview = preview
                };
                yield return Result(
                    request.ToolCall,
                    new AgentToolResult
                    {
                        ToolName = request.ToolCall.Name,
                        IsError = true,
                        Status = ToolExecutionStatus.Rejected,
                        FailureReason = string.IsNullOrWhiteSpace(approval.Reason)
                            ? "User rejected the tool call."
                            : approval.Reason,
                        Content = string.IsNullOrWhiteSpace(approval.Reason)
                            ? "用户拒绝执行该工具。"
                            : $"用户拒绝执行该工具：{approval.Reason}"
                    },
                    preview);
                yield break;
            }

            if (approval.AllowForSession)
            {
                yield return new ToolExecutionEvent
                {
                    Type = ToolExecutionEventType.SessionAllowed,
                    ToolCall = request.ToolCall,
                    AllowForSession = true,
                    SessionAllowedToolId = tool.Id
                };
            }
        }

        var result = await TryExecuteAsync(tool, request, context, cancellationToken);
        yield return Result(request.ToolCall, result, preview, tool.Risk != AgentToolRisk.ReadOnly && !result.IsError);
    }

    private static async Task<(AgentToolPreview? Preview, AgentToolResult? ErrorResult)> TryPreviewAsync(
        IAgentTool tool,
        ToolExecutionRequest request,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await tool.PreviewAsync(request.ToolCall.ArgumentsJson, context, cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (null, new AgentToolResult
            {
                ToolName = tool.Id,
                Content = $"工具 {tool.Id} 预览已取消。",
                IsError = true,
                Status = ToolExecutionStatus.Cancelled,
                FailureReason = "Tool preview was cancelled."
            });
        }
        catch (Exception ex)
        {
            return (null, new AgentToolResult
            {
                ToolName = tool.Id,
                Content = $"工具 {tool.Id} 预览失败：{ex.Message}",
                IsError = true,
                Status = ToolExecutionStatus.Exception,
                FailureReason = ex.Message
            });
        }
    }

    private static async Task<AgentToolResult> TryExecuteAsync(
        IAgentTool tool,
        ToolExecutionRequest request,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await tool.ExecuteAsync(request.ToolCall.ArgumentsJson, context, cancellationToken);
            return NormalizeResultStatus(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AgentToolResult
            {
                ToolName = tool.Id,
                Content = $"工具 {tool.Id} 执行已取消。",
                IsError = true,
                Status = ToolExecutionStatus.Cancelled,
                FailureReason = "Tool execution was cancelled."
            };
        }
        catch (Exception ex)
        {
            return new AgentToolResult
            {
                ToolName = tool.Id,
                Content = $"工具 {tool.Id} 执行失败：{ex.Message}",
                IsError = true,
                Status = ToolExecutionStatus.Exception,
                FailureReason = ex.Message
            };
        }
    }

    private static AgentToolResult NormalizeResultStatus(AgentToolResult result)
    {
        if (!result.IsError || result.Status != ToolExecutionStatus.Succeeded)
        {
            return result;
        }

        return new AgentToolResult
        {
            ToolName = result.ToolName,
            Content = result.Content,
            IsError = true,
            Status = ToolExecutionStatus.Failed,
            FailureReason = string.IsNullOrWhiteSpace(result.FailureReason)
                ? "Tool returned an error result."
                : result.FailureReason,
            ModelContent = result.ModelContent,
            WasSummarized = result.WasSummarized,
            ArtifactKind = result.ArtifactKind,
            Summary = result.Summary
        };
    }

    private static ToolExecutionEvent Result(
        ChatToolCall toolCall,
        AgentToolResult result,
        AgentToolPreview? preview = null,
        bool isMutation = false)
    {
        return new ToolExecutionEvent
        {
            Type = ToolExecutionEventType.Result,
            ToolCall = toolCall,
            Preview = preview,
            Result = ToolResultSummarizer.Summarize(result),
            IsMutation = isMutation
        };
    }

    private static ToolPermissionMode ResolvePermissionMode(ToolExecutionRequest request, IAgentTool tool)
    {
        if (request.ToolPermissionModes.TryGetValue(tool.Id, out var mode))
        {
            return mode;
        }

        return tool.Risk == AgentToolRisk.ReadOnly
            ? ToolPermissionMode.AutoReadOnly
            : ToolPermissionMode.ConfirmEachTime;
    }

    private static bool IsAutoApproved(
        ToolExecutionRequest request,
        IAgentTool tool,
        ToolPermissionMode mode)
    {
        if (request.SessionAllowedToolIds.Contains(tool.Id))
        {
            return !string.Equals(tool.Id, "run_shell", StringComparison.OrdinalIgnoreCase) ||
                   IsAllowlistedShellToolCall(request.ToolCall);
        }

        return mode switch
        {
            ToolPermissionMode.Disabled => false,
            ToolPermissionMode.AutoReadOnly => tool.Risk == AgentToolRisk.ReadOnly,
            _ => false
        };
    }

    private static bool IsAllowlistedShellToolCall(ChatToolCall toolCall)
    {
        var args = ToolJson.ParseArguments(toolCall.ArgumentsJson);
        var command = ToolJson.GetString(args, "command") ?? "";
        return ShellCommandTool.IsAllowlisted(command);
    }
}
