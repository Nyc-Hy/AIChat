namespace AIChat.Application.Llm.Routing;

public sealed record ModelProfile(
    string Id,
    string ProviderId,
    string DisplayName,
    string DefaultExecutionMode,
    int RecommendedFastToolRounds,
    int RecommendedStandardToolRounds,
    int RecommendedDeepToolRounds,
    string ToolCallPolicy,
    string ThinkingPolicy,
    string CacheStrategy,
    string PromptGuidance,
    IReadOnlyDictionary<string, string> DefaultModelParameters);

public static class ModelProfileCatalog
{
    // Catch-all for unknown provider ids. Used when the catalog has no
    // profile matching the user's settings (e.g. an old settings file
    // that pre-dated a provider rename). The shape is identical to the
    // named profile below so callers don't have to branch on whether
    // a profile was hit.
    private static readonly ModelProfile Default = new(
        "minimax-default",
        "minimax",
        "MiniMax default profile",
        "Standard",
        6,
        16,
        40,
        "Keep tool calls small and direct. Avoid mixing narrative text into tool arguments.",
        "Use interleaved thinking when available for complex edits; keep Fast tasks terse.",
        "Keep stable instructions first and isolate tool output summaries from final user-facing text.",
        "Prefer short action loops: inspect, patch, diff, verify, summarize.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minimax.reasoning_split"] = ""
        });

    private static readonly IReadOnlyList<ModelProfile> Profiles =
    [
        new(
            "minimax-coding",
            "minimax",
            "MiniMax coding profile",
            "Standard",
            6,
            16,
            40,
            "Keep tool calls small and direct. Avoid mixing narrative text into tool arguments.",
            "Use interleaved thinking when available for complex edits; keep Fast tasks terse.",
            "Keep stable instructions first and isolate tool output summaries from final user-facing text.",
            "Prefer short action loops: inspect, patch, diff, verify, summarize.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["minimax.reasoning_split"] = ""
            })
    ];

    // 2026-08-02: catalog is now single-provider. Every Resolve call
    // hits the minimax profile, so the per-provider switch is gone
    // — the Default and Profiles rows above carry the same content
    // on purpose, so an unknown-provider lookup still lands on
    // sensible MiniMax-flavored defaults instead of the old
    // "openai-compatible-default" generic shape. The fallback
    // rewrites ProviderId so callers that pass an unknown id
    // (e.g. an old settings file) still see their own id in
    // the resolved profile — the contract the previous
    // generic "openai-compatible-default" row already locked in.
    public static ModelProfile Resolve(string providerId, string modelId)
    {
        return Profiles.FirstOrDefault(profile =>
                   string.Equals(profile.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
               ?? Default with { Id = $"{providerId}-{modelId}-default", ProviderId = providerId };
    }
}
