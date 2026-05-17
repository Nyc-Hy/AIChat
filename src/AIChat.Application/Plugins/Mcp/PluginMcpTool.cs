using System.Text.Json;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Plugins.Mcp;

public sealed class PluginMcpTool : IAgentTool
{
    private readonly McpStdioClient _client;
    private readonly McpStdioServerConfig _server;
    private readonly string _mcpToolName;

    public PluginMcpTool(
        PluginManifest plugin,
        PluginMcpServerManifest server,
        McpToolDescriptor descriptor,
        McpStdioClient client)
    {
        _client = client;
        _mcpToolName = descriptor.Name;
        _server = new McpStdioServerConfig(
            server.Id,
            server.Name,
            server.Command,
            server.Arguments,
            server.WorkingDirectory,
            server.TimeoutSeconds <= 0 ? 30 : server.TimeoutSeconds);
        Id = PluginIds.NormalizeToolId(plugin.Id, $"{server.Id}_{descriptor.Name}");
        Risk = ParseRisk(server.Risk);
        Metadata = new ToolMetadata
        {
            ToolId = Id,
            Category = "MCP",
            GroupLabel = $"MCP：{server.Name}",
            DefaultPermissionMode = Risk == AgentToolRisk.ReadOnly
                ? ToolPermissionMode.AutoReadOnly
                : ToolPermissionMode.ConfirmEachTime
        };
        Definition = new ChatToolDefinition
        {
            Name = Id,
            Description = $"[MCP:{server.Name}] {descriptor.Description}",
            ParametersJson = descriptor.InputSchema.GetRawText()
        };
    }

    public string Id { get; }
    public AgentToolRisk Risk { get; }
    public ToolMetadata Metadata { get; }
    public ChatToolDefinition Definition { get; }

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"调用 MCP 工具：{_server.Name}/{_mcpToolName}",
            PreviewText = $"server={_server.Id}; tool={_mcpToolName}; args={argumentsJson}"
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var result = await _client.CallToolAsync(_server, _mcpToolName, args.RootElement, cancellationToken);
            return new AgentToolResult
            {
                ToolName = Id,
                Content = JsonSerializer.Serialize(new
                {
                    server = _server.Id,
                    tool = _mcpToolName,
                    isError = result.IsError,
                    content = result.Content
                }),
                IsError = result.IsError,
                FailureReason = result.IsError ? "MCP tool returned an error result." : ""
            };
        }
        catch (Exception ex)
        {
            return new AgentToolResult
            {
                ToolName = Id,
                Content = $"MCP 工具调用失败：{ex.Message}",
                IsError = true,
                FailureReason = ex.Message
            };
        }
    }

    private static AgentToolRisk ParseRisk(string risk)
    {
        return risk.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read_only" or "read-only" or "read" => AgentToolRisk.ReadOnly,
            "write" or "mutation" => AgentToolRisk.Write,
            _ => AgentToolRisk.Shell
        };
    }
}
