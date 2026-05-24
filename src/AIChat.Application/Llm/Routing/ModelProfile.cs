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
    private static readonly ModelProfile Default = new(
        "openai-compatible-default",
        "",
        "OpenAI-compatible default",
        "Standard",
        6,
        16,
        40,
        "Use compact JSON tool arguments and prefer project-specific tools over shell.",
        "Use the provider default reasoning behavior.",
        "Keep stable system, tool, model, and project rules before task-specific content.",
        "Be concise, tool-grounded, and avoid claiming file changes before a successful write tool result.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static readonly IReadOnlyList<ModelProfile> Profiles =
    [
        new(
            "deepseek-coding",
            "deepseek",
            "DeepSeek coding profile",
            "Standard",
            6,
            16,
            40,
            "Prefer explicit JSON tool arguments. Use read/search before edits, then diff/status after writes.",
            "Fast mode disables thinking when supported; Standard uses provider default; Deep requests high reasoning effort.",
            "DeepSeek benefits from a stable prefix and concise tool-result summaries. Keep volatile stderr/diff content late.",
            "For code changes, reason carefully before editing, but keep final answers short and evidence-based.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deepseek.thinking"] = "",
                ["deepseek.reasoning_effort"] = "",
                ["deepseek.response_format"] = ""
            }),
        new(
            "mimo-long-context",
            "tokenplan-mimo",
            "MiMo long-context coding profile",
            "Standard",
            6,
            16,
            40,
            "Use the long context for project rules and indexes, but still inspect exact files before editing.",
            "Use provider defaults; avoid unnecessary extended reasoning on small tasks.",
            "MiMo has a large context window, but cache stability still depends on fixed prompt section order.",
            "Exploit large context for broad orientation, then narrow to concrete files before making changes.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
        new(
            "minimax-coding",
            "minimax",
            "MiniMAX coding profile",
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

    public static ModelProfile Resolve(string providerId, string modelId)
    {
        return Profiles.FirstOrDefault(profile =>
                   string.Equals(profile.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
               ?? Default with { ProviderId = providerId, Id = $"{providerId}-{modelId}-default" };
    }
}
