using AIChat.Application.Tools;
using AIChat.Application.Plugins.Mcp;
using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Plugins;

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
    public IReadOnlyList<PluginSkill> Skills { get; private init; } = [];
    public IReadOnlyList<PluginMcpServerManifest> McpServers { get; private init; } = [];
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
        var mcpClient = new McpStdioClient();
        foreach (var manifest in result.Manifests)
        {
            foreach (var server in manifest.McpServers.Where(server => server.Enabled))
            {
                try
                {
                    var config = new McpStdioServerConfig(
                        server.Id,
                        server.Name,
                        server.Command,
                        server.Arguments,
                        server.WorkingDirectory,
                        server.TimeoutSeconds <= 0 ? 30 : server.TimeoutSeconds);
                    var descriptors = await mcpClient.ListToolsAsync(config, cancellationToken);
                    tools.AddRange(descriptors.Select(descriptor => new PluginMcpTool(manifest, server, descriptor, mcpClient)));
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new PluginDiagnostic(
                        PluginDiagnosticSeverity.Error,
                        $"MCP server 工具发现失败：{ex.Message}",
                        manifest.Id,
                        server.Id));
                }
            }
        }
        var skills = await PluginSkillLoader.LoadAsync(result.Manifests, cancellationToken);
        var mcpServers = result.Manifests
            .SelectMany(manifest => manifest.McpServers.Where(server => server.Enabled))
            .ToList();
        return new PluginToolProvider("local_plugins", "本地插件", tools, diagnostics)
        {
            Skills = skills,
            McpServers = mcpServers
        };
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
            Mcp.PluginMcpTool mcpTool => mcpTool.Metadata,
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
