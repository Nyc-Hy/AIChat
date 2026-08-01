using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

// Single surface for tool registration, lookup, metadata, and
// execution orchestration. The plan-3.8 refactor merged the
// previous AgentToolCatalog (registry snapshot for AgentRunner)
// and ToolExecutionService (preview + permission + approval +
// execute pipeline) into this class. AgentRunner now takes
// AgentToolRegistry directly instead of catalog + execution
// service. Find() also matches the LLM-visible
// Definition.Name (the catalog's only addition over the
// pre-merge Find), so the merged Find preserves the LLM call
// resolution semantics.
public sealed class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _toolsById;
    private readonly Dictionary<string, ToolMetadata> _metadataById;
    private readonly Lazy<IReadOnlyList<IAgentTool>> _lazyAll;
    private bool _builtinsInitialized;

    private AgentToolRegistry(
        IEnumerable<IAgentTool>? tools,
        IEnumerable<ToolMetadata>? metadata,
        Lazy<IReadOnlyList<IAgentTool>> lazyAll)
    {
        _toolsById = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        _metadataById = new Dictionary<string, ToolMetadata>(StringComparer.OrdinalIgnoreCase);
        _lazyAll = lazyAll;
        _builtinsInitialized = tools != null;

        if (tools != null)
        {
            foreach (var tool in tools)
            {
                _toolsById[tool.Id] = tool;
            }
        }

        if (metadata != null)
        {
            foreach (var m in metadata)
            {
                _metadataById[m.ToolId] = m;
            }
        }
    }

    public static AgentToolRegistry CreateDefault()
    {
        return new AgentToolRegistry(
            null,
            CreateDefaultMetadata(),
            new Lazy<IReadOnlyList<IAgentTool>>(CreateDefaultTools));
    }

    // Test-only factory: builds a registry with the supplied
    // tools pre-loaded (so Find/ExecuteAsync resolve them
    // without going through the builtin Lazy path). Used by
    // ToolExecutionServiceTests and any future tests that
    // want a minimal registry around a fake tool.
    public static AgentToolRegistry CreateForTests(IEnumerable<IAgentTool> tools)
    {
        return new AgentToolRegistry(
            tools,
            null,
            new Lazy<IReadOnlyList<IAgentTool>>(() => []));
    }

    public static async Task<AgentToolRegistry> CreateDefaultWithPluginsAsync(
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        var registry = CreateDefault();
        var provider = await Plugins.PluginToolProvider.LoadFromDirectoryAsync(pluginsDirectory, cancellationToken);
        if (provider.Tools.Count > 0)
        {
            registry.RegisterExternalProvider(provider);
            await registry.LoadExternalToolsAsync(cancellationToken);
        }

        return registry;
    }

    private void EnsureInitialized()
    {
        if (_builtinsInitialized) return;
        _builtinsInitialized = true;

        foreach (var tool in _lazyAll.Value)
        {
            _toolsById.TryAdd(tool.Id, tool);
        }
    }

    private static IReadOnlyList<IAgentTool> CreateDefaultTools()
    {
        return new IAgentTool[]
        {
            new ListFilesTool(),
            new ReadInputArtifactTool(),
            new ReadFileTool(),
            new SearchTextTool(),
            new WriteFileTool(),
            new EditFileTool(),
            new ApplyPatchTool(),
            new UpdatePlanTool(),
            new GitStatusTool(),
            new GitDiffTool(),
            new GitRestoreFileTool(),
            new GitCommitTool(),
            new RunBuildTool(),
            new RunTestTool(),
            new ShellCommandTool()
        };
    }

    private static ToolMetadata[] CreateDefaultMetadata()
    {
        return new ToolMetadata[]
        {
            new() { ToolId = "list_files", Category = "读取", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "文件浏览" },
            new() { ToolId = "read_input_artifact", Category = "读取", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "输入附件" },
            new() { ToolId = "read_file", Category = "读取", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "文件浏览" },
            new() { ToolId = "search_text", Category = "读取", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "文件浏览" },
            new() { ToolId = "write_file", Category = "写入", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "文件修改" },
            new() { ToolId = "edit_file", Category = "写入", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "文件修改" },
            new() { ToolId = "apply_patch", Category = "写入", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "文件修改" },
            new() { ToolId = "update_plan", Category = "计划", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "计划管理" },
            new() { ToolId = "git_status", Category = "Git", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "Git 操作" },
            new() { ToolId = "git_diff", Category = "Git", DefaultPermissionMode = ToolPermissionMode.AutoReadOnly, GroupLabel = "Git 操作" },
            new() { ToolId = "git_restore_file", Category = "Git", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "Git 操作" },
            new() { ToolId = "git_commit", Category = "Git", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "Git 操作" },
            new() { ToolId = "run_build", Category = "构建", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "构建与测试" },
            new() { ToolId = "run_test", Category = "构建", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "构建与测试" },
            new() { ToolId = "run_shell", Category = "Shell", DefaultPermissionMode = ToolPermissionMode.ConfirmEachTime, GroupLabel = "Shell 命令" }
        };
    }

    public IReadOnlyList<IAgentTool> All
    {
        get
        {
            EnsureInitialized();
            return _toolsById.Values.ToList();
        }
    }

    // Find by either the tool's Id or its LLM-visible
    // Definition.Name. The previous catalog.Find supported
    // both, which mattered because the LLM tool call uses
    // Definition.Name; preserving that here means the
    // registry is a drop-in replacement for the catalog.
    public IAgentTool? Find(string nameOrId)
    {
        EnsureInitialized();
        if (_toolsById.TryGetValue(nameOrId, out var tool))
        {
            return tool;
        }
        return _toolsById.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Definition.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IAgentTool> ResolveEnabled(IEnumerable<string> enabledToolIds)
    {
        EnsureInitialized();
        var enabled = enabledToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _toolsById.Values.Where(t => enabled.Contains(t.Id)).ToList();
    }

    public ToolMetadata GetMetadata(string toolId)
    {
        EnsureInitialized();
        if (_metadataById.TryGetValue(toolId, out var meta))
        {
            return meta;
        }

        var tool = Find(toolId);
        return new ToolMetadata
        {
            ToolId = toolId,
            DefaultPermissionMode = tool?.Risk == AgentToolRisk.ReadOnly
                ? ToolPermissionMode.AutoReadOnly
                : ToolPermissionMode.ConfirmEachTime,
            Category = ClassifyCategory(tool)
        };
    }

    public IReadOnlyList<(IAgentTool Tool, ToolMetadata Metadata)> AllWithMetadata()
    {
        EnsureInitialized();
        return _toolsById.Values
            .Select(t => (t, GetMetadata(t.Id)))
            .ToList();
    }

    public void RegisterExternalProvider(IExternalToolProvider provider)
    {
        _externalProviders.Add(provider);
        foreach (var metadata in provider.GetToolMetadata())
        {
            _metadataById[metadata.ToolId] = metadata;
        }
    }

    public async Task LoadExternalToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        foreach (var provider in _externalProviders)
        {
            var tools = await provider.GetToolsAsync(cancellationToken);
            foreach (var tool in tools)
            {
                _toolsById[tool.Id] = tool;
            }
        }
    }

    private readonly List<IExternalToolProvider> _externalProviders = [];

    private static string ClassifyCategory(IAgentTool? tool)
    {
        if (tool is null) return "通用";
        return tool.Id switch
        {
            "list_files" or "read_file" or "search_text" or "read_input_artifact" => "读取",
            "write_file" or "edit_file" or "apply_patch" => "写入",
            "update_plan" => "计划",
            "git_status" or "git_diff" or "git_restore_file" or "git_commit" => "Git",
            "run_build" or "run_test" => "构建",
            "run_shell" => "Shell",
            _ => "通用"
        };
    }

    // ---- Execution orchestration ----
    // Pipeline: lookup → preview → permission gate → approval
    // (if not auto-approved) → execute → summarise. Each step
    // can yield an early-Result on failure or a
    // SessionAllowed / ApprovalRequired / ApprovalRejected
    // event on the way through. The whole thing is the
    // pipeline the old ToolExecutionService ran; pulled in
    // here so AgentRunner takes a single registry instead of
    // catalog + execution service.

    public async IAsyncEnumerable<ToolExecutionEvent> ExecuteAsync(
        ToolExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tool = Find(request.ToolCall.Name);
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
