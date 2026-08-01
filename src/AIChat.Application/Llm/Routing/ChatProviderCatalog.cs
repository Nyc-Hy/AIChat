using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

// Central catalog of provider templates. The UI reads it for Settings options,
// and normalization code uses it to keep stored settings valid.
public static class ChatProviderCatalog
{
    private static readonly LlmModelCapabilities ToolCapable = new()
    {
        SupportsTools = true
    };

    private static readonly LlmModelCapabilities DeepSeekCapabilities = new()
    {
        SupportsTools = true,
        SupportsThinking = true,
        SupportsJsonOutput = true,
        SupportsPrefixCompletion = true
    };

    private static readonly LlmModelCapabilities MiniMaxCapabilities = new()
    {
        SupportsTools = true,
        SupportsInterleavedThinking = true
    };

    private static readonly IReadOnlyList<LlmModelParameterInfo> DeepSeekParameters =
    [
        new()
        {
            Id = "deepseek.thinking",
            DisplayName = "思考模式",
            Description = "DeepSeek thinking.type。默认由模型决定。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认" },
                new LlmParameterOption { Value = "enabled", DisplayName = "开启" },
                new LlmParameterOption { Value = "disabled", DisplayName = "关闭" }
            ]
        },
        new()
        {
            Id = "deepseek.reasoning_effort",
            DisplayName = "推理强度",
            Description = "DeepSeek reasoning_effort，仅在模型支持时发送。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认" },
                new LlmParameterOption { Value = "high", DisplayName = "High" },
                new LlmParameterOption { Value = "max", DisplayName = "Max" }
            ]
        },
        new()
        {
            Id = "deepseek.response_format",
            DisplayName = "输出格式",
            Description = "DeepSeek response_format。普通对话建议保持默认。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认" },
                new LlmParameterOption { Value = "json_object", DisplayName = "JSON Object" }
            ]
        }
    ];

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

    // TokenPlan exposes an OpenAI-compatible API, so the protocol is "openai"
    // even though the provider name shown to the user is Xiaomi MIMO.
    public static readonly LlmProviderInfo TokenPlanMiMo = new()
    {
        Id = "tokenplan-mimo",
        ProtocolId = "openai",
        Name = "小米 MIMO (TokenPlan)",
        DefaultBaseUrl = "https://token-plan-cn.xiaomimimo.com/v1",
        DefaultModel = "mimo-v2.5-pro",
        DefaultContextLimit = 1_000_000,
        Models =
        [
            new LlmModelInfo { Id = "mimo-v2.5-pro", DisplayName = "mimo-v2.5-pro", ContextLimit = 1_000_000, CapabilityLabel = "1M context · tools", Capabilities = ToolCapable },
            new LlmModelInfo { Id = "mimo-v2.5", DisplayName = "mimo-v2.5", ContextLimit = 1_000_000, CapabilityLabel = "1M context · tools", Capabilities = ToolCapable }
        ]
    };

    public static readonly LlmProviderInfo DeepSeek = new()
    {
        Id = "deepseek",
        ProtocolId = "openai",
        Name = "DeepSeek",
        DefaultBaseUrl = "https://api.deepseek.com",
        DefaultModel = "deepseek-v4-pro",
        DefaultContextLimit = 128_000,
        Models =
        [
            new LlmModelInfo { Id = "deepseek-v4-pro", DisplayName = "deepseek-v4-pro", ContextLimit = 128_000, CapabilityLabel = "thinking · tools · JSON", Capabilities = DeepSeekCapabilities, Parameters = DeepSeekParameters },
            new LlmModelInfo { Id = "deepseek-v4-flash", DisplayName = "deepseek-v4-flash", ContextLimit = 128_000, CapabilityLabel = "thinking · tools · JSON", Capabilities = DeepSeekCapabilities, Parameters = DeepSeekParameters },
            new LlmModelInfo { Id = "deepseek-chat", DisplayName = "deepseek-chat", ContextLimit = 64_000, CapabilityLabel = "tools · JSON", Capabilities = DeepSeekCapabilities, Parameters = DeepSeekParameters },
            new LlmModelInfo { Id = "deepseek-reasoner", DisplayName = "deepseek-reasoner", ContextLimit = 64_000, CapabilityLabel = "reasoning · JSON", Capabilities = DeepSeekCapabilities, Parameters = DeepSeekParameters }
        ]
    };

    public static readonly LlmProviderInfo MiniMax = new()
    {
        Id = "minimax",
        ProtocolId = "openai",
        Name = "MiniMax",
        DefaultBaseUrl = "https://api.minimax.io/v1",
        DefaultModel = "MiniMax-M2.1",
        DefaultContextLimit = 200_000,
        Models =
        [
            new LlmModelInfo { Id = "MiniMax-M2.1", DisplayName = "MiniMax-M2.1", ContextLimit = 200_000, CapabilityLabel = "interleaved thinking · tools", Capabilities = MiniMaxCapabilities, Parameters = MiniMaxParameters },
            new LlmModelInfo { Id = "MiniMax-M2", DisplayName = "MiniMax-M2", ContextLimit = 200_000, CapabilityLabel = "interleaved thinking · tools", Capabilities = MiniMaxCapabilities, Parameters = MiniMaxParameters }
        ]
    };

    public static readonly LlmProviderInfo OpenAICompatible = new()
    {
        Id = "openai-compatible",
        ProtocolId = "openai",
        Name = "OpenAI-compatible",
        DefaultBaseUrl = "https://api.openai.com/v1",
        DefaultModel = "gpt-5",
        DefaultContextLimit = 400_000,
        Models =
        [
            // OpenAI's current flagship tier (2026-08). 400K context,
            // tools + vision. The "gpt-5-mini" entry is the price/quality
            // sweet spot for day-to-day work; the older 4.1 line stays
            // because some users still have it provisioned and we don't
            // want to break their existing settings.
            new LlmModelInfo { Id = "gpt-5", DisplayName = "gpt-5", ContextLimit = 400_000, CapabilityLabel = "tools · vision", Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true } },
            new LlmModelInfo { Id = "gpt-5-mini", DisplayName = "gpt-5-mini", ContextLimit = 400_000, CapabilityLabel = "tools · vision", Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true } },
            new LlmModelInfo { Id = "gpt-4.1", DisplayName = "gpt-4.1", ContextLimit = 1_000_000, CapabilityLabel = "tools · vision · 1M ctx", Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true } },
            new LlmModelInfo { Id = "gpt-4.1-mini", DisplayName = "gpt-4.1-mini", ContextLimit = 1_000_000, CapabilityLabel = "tools · vision · 1M ctx", Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true } },
            // For users running against a non-OpenAI endpoint (a self-hosted
            // vLLM, llama.cpp, or a private proxy), the literal model id
            // is typed in the Settings textbox and resolved via
            // ResolveModel's "non-empty user-typed id" path. Kept here so
            // the dropdown isn't empty on a fresh install before the user
            // types their real id.
            new LlmModelInfo { Id = "custom-model", DisplayName = "custom-model", ContextLimit = 128_000, CapabilityLabel = "tools", Capabilities = ToolCapable }
        ]
    };

    public static readonly LlmProviderInfo Anthropic = new()
    {
        Id = "anthropic",
        ProtocolId = "anthropic",
        Name = "Anthropic",
        DefaultBaseUrl = "https://api.anthropic.com",
        // Opus 4.6 is the current Anthropic flagship as of 2026-08.
        // Sonnet 4.5 and Haiku 3.5 (the small/fast tier) round out the
        // default lineup. We deliberately drop claude-3-5-* from the
        // default Models list — users with old settings still resolve
        // through ResolveModel's "non-empty user-typed id" path, so
        // existing setups keep working — but the dropdown now reflects
        // the 4.x generation by default.
        DefaultModel = "claude-opus-4-6",
        DefaultContextLimit = 200_000,
        Models =
        [
            new LlmModelInfo
            {
                Id = "claude-opus-4-6",
                DisplayName = "claude-opus-4-6",
                ContextLimit = 200_000,
                CapabilityLabel = "tools · vision · thinking",
                Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true, SupportsThinking = true }
            },
            new LlmModelInfo
            {
                Id = "claude-sonnet-4-5",
                DisplayName = "claude-sonnet-4-5",
                ContextLimit = 200_000,
                CapabilityLabel = "tools · vision · thinking",
                Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true, SupportsThinking = true }
            },
            new LlmModelInfo
            {
                Id = "claude-3-5-haiku-latest",
                DisplayName = "claude-3-5-haiku-latest",
                ContextLimit = 200_000,
                CapabilityLabel = "tools · vision (fast)",
                Capabilities = new LlmModelCapabilities { SupportsTools = true, SupportsVision = true }
            }
        ]
    };

    public static IReadOnlyList<LlmProviderInfo> All { get; } = [TokenPlanMiMo, DeepSeek, MiniMax, OpenAICompatible, Anthropic];

    // Resolve methods deliberately fall back to a safe default so old settings
    // files or renamed provider IDs do not break startup.
    public static LlmProviderInfo Resolve(string? providerIdOrName)
    {
        return All.FirstOrDefault(provider =>
                   string.Equals(provider.Id, providerIdOrName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(provider.Name, providerIdOrName, StringComparison.OrdinalIgnoreCase))
               ?? TokenPlanMiMo;
    }

    // Resolves a model id against the catalog. Behavior:
    // - exact id match in the provider's Models list → return that model
    //   (preserves all capabilities / context limit from the catalog row)
    // - non-empty user-typed id with no match → return a synthetic
    //   LlmModelInfo carrying the user's id verbatim. The catalog is a
    //   defaults source, not a gate; users running a model the catalog
    //   doesn't know about (new release, private deployment, beta
    //   channel) must be able to bind to it. The earlier shape silently
    //   fell back to Models.First() for non-OpenAI-compatible providers,
    //   which clobbered the user's input on save and made "I can't bind
    //   a model" a daily-driver frustration.
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
