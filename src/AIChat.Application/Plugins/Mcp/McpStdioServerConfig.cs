namespace AIChat.Application.Plugins.Mcp;

public sealed record McpStdioServerConfig(
    string Id,
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int TimeoutSeconds);
