namespace AIChat.Domain.Chat;

// 2026-08-05: token usage returned by the provider for a
// single chat-completion call. MiniMax (and other
// OpenAI-compatible providers that honor prompt caching)
// surface this in the streaming response's final chunk
// when the request includes `stream_options:
// { include_usage: true }`. The breakdown:
//   - PromptTokens: total input tokens billed for this
//     call (includes the cached + uncached split)
//   - CompletionTokens: tokens the model generated
//   - CachedTokens: portion of PromptTokens served from
//     the platform's prompt cache (1/5 input price on
//     M3; the cache is automatic, no user toggle). 0 on
//     models that don't expose a cache (M2.7 might or
//     might not — TBD per platform).
//
// Surfaced in the Status bar / activity feed so a daily
// driver can see the cache hit rate on every run ("64%
// cache 命中" = the 114 / 177 split from a 2026-08-05
// curl probe). Init-only because the payload is parsed
// once from a JSON chunk and never mutated.
public sealed class ChatUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int CachedTokens { get; init; }

    public int TotalTokens => PromptTokens + CompletionTokens;

    public double CacheHitPercent => PromptTokens > 0
        ? Math.Round((double)CachedTokens / PromptTokens * 100.0, 1)
        : 0.0;
}
