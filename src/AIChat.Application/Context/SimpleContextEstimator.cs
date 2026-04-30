using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Domain.Context;

namespace AIChat.Application.Context;

// Fast, dependency-free context estimator for the MVP. It uses a rough
// characters-to-tokens ratio so the UI can teach the idea of context budgeting
// before a real tokenizer is introduced.
public sealed class SimpleContextEstimator : IContextEstimator
{
    public ContextUsage Estimate(IReadOnlyList<ChatMessage> messages, AppSettings settings)
    {
        var chars = messages.Sum(message => message.Content.Length);
        // English and Chinese text tokenize differently; 3.6 chars/token is a
        // practical placeholder, not a billing-grade number.
        var estimatedTokens = Math.Max(0, (int)Math.Ceiling(chars / 3.6));
        return new ContextUsage
        {
            CurrentTokens = estimatedTokens,
            // Keep a smaller active conversation budget even when the model has a
            // huge context window; future Agent instructions/tools need headroom.
            ConversationLimit = Math.Min(settings.ModelContextLimit, 64_000),
            ModelLimit = settings.ModelContextLimit
        };
    }
}
