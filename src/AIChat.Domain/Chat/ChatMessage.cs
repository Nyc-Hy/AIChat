namespace AIChat.Domain.Chat;

// One message inside a conversation. Domain models stay UI-agnostic so they can
// be stored, sent to providers, or reused by future Agent layers.
public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    // System/User/Assistant is the minimal role set used by most chat APIs.
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    // Tool result messages use these fields so providers can match a result to
    // the assistant tool call that requested it.
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string AgentRunId { get; set; } = "";
    // Assistant messages can carry requested tool calls in the Agent transcript.
    public List<ChatToolCall> ToolCalls { get; set; } = [];
    // Visible assistant messages keep a readable chain of tool calls/results.
    public List<ChatToolTrace> ToolTraces { get; set; } = [];
    // Stored with the message so failed assistant replies remain visible after restart.
    public bool IsError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
