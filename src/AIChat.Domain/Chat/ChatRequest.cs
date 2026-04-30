namespace AIChat.Domain.Chat;

// Provider-neutral request used by the application layer. Each provider is
// responsible for mapping these fields to its own HTTP payload.
public sealed class ChatRequest
{
    public required string Model { get; init; }
    // The caller passes the already-selected conversation context.
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public double Temperature { get; init; } = 0.3;
    // Optional tools sent to the model for selection. Empty means plain chat.
    public IReadOnlyList<ChatToolDefinition> Tools { get; init; } = [];
}
