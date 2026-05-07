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
                    IsError = true
                });
            yield break;
        }

        var context = new AgentToolContext
        {
            ProjectPath = request.ProjectPath,
            InputArtifacts = request.InputArtifacts
        };
        var preview = await tool.PreviewAsync(request.ToolCall.ArgumentsJson, context, cancellationToken);
        var mode = ResolvePermissionMode(request, tool);
        if (mode == ToolPermissionMode.Disabled)
        {
            yield return Result(
                request.ToolCall,
                new AgentToolResult
                {
                    ToolName = request.ToolCall.Name,
                    IsError = true,
                    Content = "该工具已在设置中关闭，未执行。"
                },
                preview);
            yield break;
        }

        if (!IsAutoApproved(tool, mode, request.SessionAllowedToolIds))
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

        var result = await tool.ExecuteAsync(request.ToolCall.ArgumentsJson, context, cancellationToken);
        yield return Result(request.ToolCall, result, preview, tool.Risk != AgentToolRisk.ReadOnly && !result.IsError);
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
        IAgentTool tool,
        ToolPermissionMode mode,
        IReadOnlySet<string> sessionAllowedToolIds)
    {
        if (sessionAllowedToolIds.Contains(tool.Id))
        {
            return true;
        }

        return mode switch
        {
            ToolPermissionMode.Disabled => false,
            ToolPermissionMode.AutoReadOnly => tool.Risk == AgentToolRisk.ReadOnly,
            _ => false
        };
    }
}
