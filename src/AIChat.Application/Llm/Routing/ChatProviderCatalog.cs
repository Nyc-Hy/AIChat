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

    public static IReadOnlyList<LlmProviderInfo> All { get; } = [TokenPlanMiMo, DeepSeek, MiniMax];

    // Resolve methods deliberately fall back to a safe default so old settings
    // files or renamed provider IDs do not break startup.
    public static LlmProviderInfo Resolve(string? providerIdOrName)
    {
        return All.FirstOrDefault(provider =>
                   string.Equals(provider.Id, providerIdOrName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(provider.Name, providerIdOrName, StringComparison.OrdinalIgnoreCase))
               ?? TokenPlanMiMo;
    }

    public static LlmModelInfo ResolveModel(string? providerIdOrName, string? modelId)
    {
        var provider = Resolve(providerIdOrName);
        return provider.Models.FirstOrDefault(model =>
                   string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
               ?? provider.Models.First();
    }
}
