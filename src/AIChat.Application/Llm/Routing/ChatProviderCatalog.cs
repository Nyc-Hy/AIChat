using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

// Central catalog of provider templates. The UI reads it for Settings options,
// and normalization code uses it to keep stored settings valid.
//
// 2026-08-02: pruned to a single provider. AIChat ships with MiniMax only
// (M3 is the current flagship as of 2026-08). The other catalog rows were
// for OpenAI-compatible / DeepSeek / Xiaomi MIMO / Anthropic; they were
// removed because the daily-driver flow only needs one model. The catalog
// is the dropdown source — anything not listed here won't show up in
// Settings. Users with old settings files whose ProviderId / Model field
// references a removed provider land on MiniMax through Resolve()'s
// fallback, with the model id preserved verbatim through ResolveModel's
// "non-empty user-typed id" path (so a self-hosted MiniMax-style endpoint
// with a private model id still works).
public static class ChatProviderCatalog
{
    private static readonly LlmModelCapabilities ToolCapable = new()
    {
        SupportsTools = true
    };

    private static readonly LlmModelCapabilities MiniMaxCapabilities = new()
    {
        SupportsTools = true,
        SupportsInterleavedThinking = true
    };

    private static readonly IReadOnlyList<LlmModelParameterInfo> MiniMaxParameters =
    [
        new()
        {
            Id = "minimax.reasoning_split",
            DisplayName = "思考分离",
            Description = "MiniMax OpenAI-compatible reasoning_split。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认" },
                new LlmParameterOption { Value = "true", DisplayName = "开启" },
                new LlmParameterOption { Value = "false", DisplayName = "关闭" }
            ]
        }
    ];

    public static readonly LlmProviderInfo MiniMax = new()
    {
        Id = "minimax",
        ProtocolId = "openai",
        Name = "MiniMax",
        DefaultBaseUrl = "https://api.minimax.io/v1",
        DefaultModel = "MiniMax-M3",
        DefaultContextLimit = 200_000,
        Models =
        [
            // M3 is the current flagship as of 2026-08. We deliberately
            // list a single model so the Settings dropdown reflects "the
            // latest one" without per-release list churn — the prior
            // M2 / M2.1 line is reachable through the free-form model id
            // textbox (ResolveModel's "non-empty user-typed id" path) for
            // users who explicitly want to pin an older model.
            new LlmModelInfo
            {
                Id = "MiniMax-M3",
                DisplayName = "MiniMax-M3",
                ContextLimit = 200_000,
                CapabilityLabel = "interleaved thinking · tools",
                Capabilities = MiniMaxCapabilities,
                Parameters = MiniMaxParameters
            }
        ]
    };

    public static IReadOnlyList<LlmProviderInfo> All { get; } = [MiniMax];

    // Resolve methods deliberately fall back to MiniMax so old settings
    // files (or a renamed provider id) do not break startup. The catalog
    // is now single-provider, so every unknown input collapses to the
    // only ship target.
    public static LlmProviderInfo Resolve(string? providerIdOrName)
    {
        return All.FirstOrDefault(provider =>
                   string.Equals(provider.Id, providerIdOrName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(provider.Name, providerIdOrName, StringComparison.OrdinalIgnoreCase))
               ?? MiniMax;
    }

    // Resolves a model id against the catalog. Behavior:
    // - exact id match in the provider's Models list → return that model
    //   (preserves all capabilities / context limit from the catalog row)
    // - non-empty user-typed id with no match → return a synthetic
    //   LlmModelInfo carrying the user's id verbatim. The catalog is a
    //   defaults source, not a gate; users running MiniMax against a
    //   private deployment / proxy with a custom model id must be able
    //   to bind to it. The earlier shape silently fell back to
    //   Models.First() for non-OpenAI-compatible providers, which
    //   clobbered the user's input on save and made "I can't bind a
    //   model" a daily-driver frustration.
    // - empty / whitespace modelId → fall back to the first catalog
    //   model. This is the only path that needs the fallback, and it
    //   keeps "old settings files with an empty Model field" working.
    public static LlmModelInfo ResolveModel(string? providerIdOrName, string? modelId)
    {
        var provider = Resolve(providerIdOrName);
        var trimmedId = modelId?.Trim();

        var exactMatch = provider.Models.FirstOrDefault(model =>
            string.Equals(model.Id, trimmedId, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        if (!string.IsNullOrWhiteSpace(trimmedId))
        {
            return new LlmModelInfo
            {
                Id = trimmedId,
                DisplayName = trimmedId,
                ContextLimit = provider.DefaultContextLimit,
                CapabilityLabel = "tools",
                Capabilities = ToolCapable
            };
        }

        return provider.Models.First();
    }
}
