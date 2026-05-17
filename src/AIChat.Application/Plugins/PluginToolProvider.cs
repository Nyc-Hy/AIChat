using AIChat.Application.Tools;

namespace AIChat.Application.Plugins;

public sealed class PluginToolProvider : IExternalToolProvider
{
    private readonly IReadOnlyList<ToolMetadata> _metadata;

    public PluginToolProvider(string id, string name, IReadOnlyList<PluginCommandTool> tools)
    {
        Id = id;
        Name = name;
        Tools = tools;
        _metadata = tools.Select(tool => tool.Metadata).ToList();
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<PluginCommandTool> Tools { get; }

    public static async Task<PluginToolProvider> LoadFromDirectoryAsync(
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        var manifests = await PluginManifestLoader.LoadDirectoryAsync(pluginsDirectory, cancellationToken);
        var tools = manifests
            .SelectMany(manifest => manifest.Tools.Select(tool => new PluginCommandTool(manifest, tool)))
            .ToList();
        return new PluginToolProvider("local_plugins", "本地插件", tools);
    }

    public Task<IReadOnlyList<IAgentTool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IAgentTool>>(Tools);
    }

    public IReadOnlyList<ToolMetadata> GetToolMetadata() => _metadata;
}
