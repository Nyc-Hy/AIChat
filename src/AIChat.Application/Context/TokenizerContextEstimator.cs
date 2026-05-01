using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;
using SharpToken;

namespace AIChat.Application.Context;

/// <summary>
/// Token-accurate context estimator backed by Tiktoken (via SharpToken).
/// Falls back to the character-based heuristic when encoding lookup fails.
/// </summary>
public sealed class TokenizerContextEstimator : IContextEstimator
{
    private readonly GptEncoding _encoding;
    private readonly SimpleContextEstimator _fallback = new();

    public TokenizerContextEstimator(string encodingName = "cl100k_base")
    {
        _encoding = GptEncoding.GetEncoding(encodingName);
    }

    public ContextUsage Estimate(IReadOnlyList<ChatMessage> messages, AppSettings settings)
    {
        try
        {
            var totalTokens = 0;
            foreach (var message in messages)
            {
                // Each message has ~4 tokens of overhead (role, separators)
                totalTokens += 4;
                totalTokens += _encoding.Encode(message.Content).Count;

                if (message.ToolCalls is { Count: > 0 })
                {
                    foreach (var toolCall in message.ToolCalls)
                    {
                        totalTokens += 4;
                        totalTokens += _encoding.Encode(toolCall.Name ?? "").Count;
                        totalTokens += _encoding.Encode(toolCall.ArgumentsJson ?? "").Count;
                    }
                }
            }

            // System message overhead
            totalTokens += 2;

            var ratio = settings.ConversationContextRatio is > 0 and <= 1
                ? settings.ConversationContextRatio
                : 0.7;
            var conversationLimit = (int)(settings.ModelContextLimit * ratio);
            conversationLimit = Math.Max(conversationLimit, 4_000);

            return new ContextUsage
            {
                CurrentTokens = totalTokens,
                ConversationLimit = conversationLimit,
                ModelLimit = settings.ModelContextLimit
            };
        }
        catch
        {
            // Encoding not available — fall back to character heuristic
            return _fallback.Estimate(messages, settings);
        }
    }
}
