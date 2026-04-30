using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ToolExecutionEvent
{
    public required ToolExecutionEventType Type { get; init; }
    public ChatToolCall? ToolCall { get; init; }
    public AgentToolPreview? Preview { get; init; }
    public AgentToolResult? Result { get; init; }
    public bool IsMutation { get; init; }
    public bool AllowForSession { get; init; }
    public string SessionAllowedToolId { get; init; } = "";
}
