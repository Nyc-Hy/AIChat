using System.Text.Json;

namespace AIChat.Application.Plugins.Mcp;

public sealed record McpToolDescriptor(
    string Name,
    string Description,
    JsonElement InputSchema);
