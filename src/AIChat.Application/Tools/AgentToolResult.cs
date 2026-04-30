namespace AIChat.Application.Tools;

public sealed class AgentToolResult
{
    public required string ToolName { get; init; }
    public required string Content { get; init; }
    public bool IsError { get; init; }
}
