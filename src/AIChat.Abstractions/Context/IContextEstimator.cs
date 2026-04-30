using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Domain.Context;

namespace AIChat.Abstractions.Context;

// Boundary for estimating how much context a conversation consumes. Swapping
// this implementation later is where a tokenizer-backed estimator would fit.
public interface IContextEstimator
{
    ContextUsage Estimate(IReadOnlyList<ChatMessage> messages, AppSettings settings);
}
