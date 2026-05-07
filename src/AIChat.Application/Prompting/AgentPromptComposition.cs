using AIChat.Domain.Chat;

namespace AIChat.Application.Prompting;

public sealed class AgentPromptComposition
{
    public required AgentPromptProfile Profile { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public int EstimatedTokens { get; init; }
}
