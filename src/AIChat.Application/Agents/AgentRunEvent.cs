using AIChat.Domain.Chat;
using AIChat.Application.Tools;

namespace AIChat.Application.Agents;

public sealed class AgentRunEvent
{
    public required AgentRunEventType Type { get; init; }
    public string Content { get; init; } = "";
    public string RawJson { get; init; } = "";
    public ChatToolCall? ToolCall { get; init; }
    public AgentToolPreview? ToolPreview { get; init; }
    public AgentToolResult? ToolResult { get; init; }
}
