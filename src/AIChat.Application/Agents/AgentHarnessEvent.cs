using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public sealed class AgentHarnessEvent
{
    public required AgentHarnessEventType Type { get; init; }
    public AgentRun? Run { get; init; }
    public AgentStep? Step { get; init; }
    public string Content { get; init; } = "";
    public string RawJson { get; init; } = "";
    public ChatToolCall? ToolCall { get; init; }
    public AgentToolPreview? ToolPreview { get; init; }
    public AgentToolResult? ToolResult { get; init; }
}
