namespace AIChat.Application.Plugins.Mcp;

public sealed record McpToolCallResult(
    string Content,
    bool IsError);
