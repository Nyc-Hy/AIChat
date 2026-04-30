namespace AIChat.Domain.Chat;

// Captures one round-trip to a model provider. For Agent development, this is
// the first observability hook: what did we send, what came back, and when?
public sealed class LlmCallDetail
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string UserMessageId { get; set; } = "";
    public string AssistantMessageId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "进行中";
    // Pretty JSON snapshot of the request at send time.
    public string RequestJson { get; set; } = "";
    // Pretty JSON snapshot of the final response and selected raw streaming events.
    public string ResponseJson { get; set; } = "";
}
