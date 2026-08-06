using AIChat.Abstractions.Configuration;
using AIChat.Application.Tools;

namespace AIChat.Application.Plugins;

// Loads the local plugin manifests from disk and turns their
// declared tools into runtime IAgentTool instances. The previous
// shape also loaded MCP servers and "skills" — both of those
// subsystems were deleted in the v1.0 refactor (MCP never
// shipped UI, "skills" was wired into the prompt builder but
// no plugin ever declared one). Today every plugin is a
// command-style tool.
public sealed class PluginToolProvider : IExternalToolProvider
{
    private readonly IReadOnlyList<ToolMetadata> _metadata;

    public PluginToolProvider(
        string id,
        string name,
        IReadOnlyList<IAgentTool> tools,
        IReadOnlyList<PluginDiagnostic>? diagnostics = null)
    {
        Id = id;
        Name = name;
        Tools = tools;
        Diagnostics = diagnostics ?? [];
        _metadata = tools.Select(GetMetadata).ToList();
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<IAgentTool> Tools { get; }
    public IReadOnlyList<PluginDiagnostic> Diagnostics { get; }

    public static async Task<PluginToolProvider> LoadFromDirectoryAsync(
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = await PluginManifestLoader.LoadDirectoryWithDiagnosticsAsync(pluginsDirectory, cancellationToken);
        var tools = result.Manifests
            .SelectMany(manifest => manifest.Tools.Select(tool => new PluginCommandTool(manifest, tool)))
            .Cast<IAgentTool>()
            .ToList();
        var diagnostics = result.Diagnostics.ToList();
        return new PluginToolProvider("local_plugins", "本地插件", tools, diagnostics);
    }

    public Task<IReadOnlyList<IAgentTool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IAgentTool>>(Tools);
    }

    public IReadOnlyList<ToolMetadata> GetToolMetadata() => _metadata;

    private static ToolMetadata GetMetadata(IAgentTool tool)
    {
        return tool switch
        {
            PluginCommandTool commandTool => commandTool.Metadata,
            _ => new ToolMetadata
            {
                ToolId = tool.Id,
                Category = "插件",
                GroupLabel = "插件工具",
                DefaultPermissionMode = tool.Risk == AgentToolRisk.ReadOnly
                    ? ToolPermissionMode.AutoReadOnly
                    : ToolPermissionMode.ConfirmEachTime
            }
        };
    }
}
