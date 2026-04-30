namespace AIChat.Domain.Chat;

// UI-facing audit trail for a tool call made during one assistant turn. Unlike
// ChatRole.Tool messages, traces are persisted with the visible assistant
// message so the user can inspect the Agent chain after the run finishes.
public sealed class ChatToolTrace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
    public string ResultContent { get; set; } = "";
    public bool IsError { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
}
