using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Tools;

public sealed class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _toolsById;
    private readonly Dictionary<string, ToolMetadata> _metadataById;
    private readonly Lazy<IReadOnlyList<IAgentTool>> _lazyAll;
    private bool _initialized;

    private AgentToolRegistry(
        IEnumerable<IAgentTool>? tools,
        IEnumerable<ToolMetadata>? metadata,
        Lazy<IReadOnlyList<IAgentTool>> lazyAll)
    {
        _toolsById = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        _metadataById = new Dictionary<string, ToolMetadata>(StringComparer.OrdinalIgnoreCase);
        _lazyAll = lazyAll;
        _initialized = false;

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
            null,
            new Lazy<IReadOnlyList<IAgentTool>>(CreateDefaultTools));
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        if (_toolsById.Count == 0)
        {
            foreach (var tool in _lazyAll.Value)
            {
                _toolsById[tool.Id] = tool;
            }
        }
    }

    private static IReadOnlyList<IAgentTool> CreateDefaultTools()
    {
        return new IAgentTool[]
        {
            new ListFilesTool(),
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

    public IAgentTool? Find(string toolId)
    {
        EnsureInitialized();
        return _toolsById.TryGetValue(toolId, out var tool) ? tool : null;
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
        // Tools are fetched lazily; this stores the provider reference for later use.
        // Call LoadExternalToolsAsync to populate.
        _externalProviders.Add(provider);
    }

    public async Task LoadExternalToolsAsync(CancellationToken cancellationToken = default)
    {
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
            "list_files" or "read_file" or "search_text" => "读取",
            "write_file" or "edit_file" or "apply_patch" => "写入",
            "update_plan" => "计划",
            "git_status" or "git_diff" or "git_restore_file" or "git_commit" => "Git",
            "run_build" or "run_test" => "构建",
            "run_shell" => "Shell",
            _ => "通用"
        };
    }
}
