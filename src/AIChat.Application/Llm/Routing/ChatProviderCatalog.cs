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
//
// 2026-08-04: two shipping MiniMax tiers (M3 flagship, M3-highspeed
// same-model faster variant). M2.7 / M2.7-highspeed used to be in the
// dropdown too (for the brief 2026-08-03 window when Coding Plan keys
// returned 401 on M3) but the platform unified the billing surfaces
// overnight and Coding Plan keys (sk-cp-…) now authenticate against
// M3 too (curl-confirmed 200 on 2026-08-05). The M2.7 catalog rows
// are gone; the 1-click "切到 M2.7 试试" failure-row button that
// pointed users at the now-redundant tier is also gone (see commit
// that lands this header).
//
// The free-form "type any model id" path is still the escape hatch
// for M2 / M2.5 / private deployments — ResolveModel's "non-empty
// user-typed id" branch returns a synthetic LlmModelInfo with the
// user-supplied id verbatim. A user who already has
// `model: MiniMax-M2.7` in their settings.json is preserved through
// that path (with the M2.7 parameter shape from MiniMaxM27Parameters
// applied).
public static class ChatProviderCatalog
{
    private static readonly LlmModelCapabilities ToolCapable = new()
    {
        SupportsTools = true
    };

    private static readonly LlmModelCapabilities MiniMaxCapabilities = new()
    {
        SupportsTools = true,
        SupportsThinking = true,
        SupportsInterleavedThinking = true,
        SupportsJsonOutput = true,
        SupportsVision = true
    };

    // 2026-08-04: MiniMax unified the Coding Plan and
    // Token Plan billing surfaces so that Coding Plan
    // keys (sk-cp-…) now authenticate against M3 / M3-
    // highspeed too. The earlier split (Coding Plan →
    // M2.7, Token Plan → M3) was the 2026-08-03
    // reality; the platform quietly changed it
    // overnight and a curl probe on 2026-08-05 now
    // returns 200 from M3 with a Coding Plan key. The
    // M2.7 / M2.7-highspeed catalog rows that landed
    // in 65b61f7 to cover the old "key works for M2.7
    // but not M3" trap are now actively misleading
    // (the failure-row "切到 M2.7 试试" button points
    // users away from a model their key actually
    // pays for). Both rows are dropped; existing user
    // settings.json with `model: MiniMax-M2.7` still
    // works through the user-typed-id path in
    // ResolveModel, but the dropdown no longer
    // surfaces M2.7 as a pickable option.
    //
    // Per-model parameters. Each model gets its own copy because
    // Parameters is init-only on LlmModelInfo, and MiniMax's
    // reasoning_split / top_p knobs are model-level decisions the
    // user should be able to flip without them leaking to a
    // model that doesn't honor them.
    private static readonly IReadOnlyList<LlmModelParameterInfo> MiniMaxM3Parameters =
    [
        new()
        {
            // 2026-08-04: MiniMax M3 native thinking-mode switch
            // (per the official M3 README: "M3 supports three
            // reasoning modes through the thinking parameter:
            // enabled — always reasons, adaptive — M3 decides,
            // disabled — never reasons"). This is the knob the
            // daily driver actually wants when they say "思考模式
            // 的开关" — flipping the model's behavior between
            //  always-think / sometimes-think / never-think on a
            // per-call basis. M3-only — M2.7 / M2.x predates the
            // switch and always reasons when the prompt warrants
            // it (no user-facing toggle). Sent as a top-level
            // string field; M3's OpenAI-compatible path accepts
            // the simple form. Default "默认" leaves the
            // platform's recommended behavior in place (adaptive
            // for the M3 base model, per the README's "adaptive"
            // description matching M3's marketed behavior).
            Id = "minimax.thinking",
            DisplayName = "思考模式",
            Description = "M3 思考模式 — enabled: 总思考 / adaptive: M3 自适应 / disabled: 关闭（最低延迟）。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认 (adaptive)" },
                new LlmParameterOption { Value = "enabled", DisplayName = "总是思考" },
                new LlmParameterOption { Value = "adaptive", DisplayName = "自适应" },
                new LlmParameterOption { Value = "disabled", DisplayName = "关闭思考" }
            ]
        },
        new()
        {
            // 2026-08-04: MiniMax OpenAI-compatible extension. When
            // true, the API returns reasoning_content as a sibling
            // of content in the chat completion response (and the
            // streaming delta emits `reasoning_content` deltas
            // before the final `content` deltas). The OpenAI
            // provider reads reasoning_content today (lines 182-199
            // and 410-417 of OpenAICompatibleChatProvider) — the
            // model side just needs the user to opt in via this
            // parameter. Default off because the M3 base model
            // already produces clean, complete answers for the
            // daily-driver flow; on long Agent runs the user may
            // want to see the reasoning chain for debugging.
            Id = "minimax.reasoning_split",
            DisplayName = "思考分离",
            Description = "MiniMax OpenAI-compatible reasoning_split — true 时 reasoning_content 作为 content 的兄弟字段返回。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认" },
                new LlmParameterOption { Value = "true", DisplayName = "开启" },
                new LlmParameterOption { Value = "false", DisplayName = "关闭" }
            ]
        },
        new()
        {
            // 2026-08-04: nucleus sampling. MiniMax honors top_p on
            // the same /chat/completions surface as OpenAI. We
            // surface it because the recommended M3 sampling
            // parameters (per MiniMax docs) are temperature≈0.3 +
            // top_p≈0.95 for coding tasks — a daily driver who
            // wants reproducible refactor output should be able to
            // tune top_p from the Settings modal, not via a
            // hidden code path.
            Id = "top_p",
            DisplayName = "top_p",
            Description = "nucleus sampling — 0~1, 越大输出越多样。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认" },
                new LlmParameterOption { Value = "0.1", DisplayName = "0.1 (集中)" },
                new LlmParameterOption { Value = "0.5", DisplayName = "0.5" },
                new LlmParameterOption { Value = "0.9", DisplayName = "0.9" },
                new LlmParameterOption { Value = "0.95", DisplayName = "0.95 (推荐)" },
                new LlmParameterOption { Value = "1.0", DisplayName = "1.0 (全开)" }
            ]
        },
        new()
        {
            // 2026-08-04: parallel tool calls. MiniMax M3 supports
            // firing multiple tool calls in the same turn
            // (e.g. read 3 files in parallel) when this is true.
            // Default true — M3's tool loop is built around
            // batching, and disabling it would slow down every
            // read_file / search_text dispatch chain. Surfaced
            // here for the user who runs into a "too many parallel
            // calls" rate limit and wants a single-flight fallback.
            Id = "parallel_tool_calls",
            DisplayName = "并行工具调用",
            Description = "true 时允许单轮多 tool call（读多文件/多 search 并行）。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认 (true)" },
                new LlmParameterOption { Value = "true", DisplayName = "开启" },
                new LlmParameterOption { Value = "false", DisplayName = "关闭" }
            ]
        },
        new()
        {
            // 2026-08-04: structured JSON output. Standard
            // OpenAI-compatible response_format — M3 honors it
            // for users who want machine-parseable output (e.g.
            // a daily driver piping an agent's response into a
            // downstream tool). When selected, the provider
            // injects {"type": "json_object"} into the request
            // payload and the model is forced to emit valid
            // JSON. Empty default leaves the platform's text
            // mode in place. Note: the prompt must contain the
            // word "json" in some form for the API to honor
            // this (OpenAI's own validation rule, which the
            // M3 OpenAI-compatible surface inherits).
            Id = "response_format",
            DisplayName = "JSON 模式",
            Description = "强制模型输出合法 JSON。Prompt 中需要包含「json」字样才会生效。",
            DefaultValue = "",
            Options =
            [
                new LlmParameterOption { Value = "", DisplayName = "默认 (文本)" },
                new LlmParameterOption { Value = "json_object", DisplayName = "JSON 对象" }
            ]
        }
    ];

    public static readonly LlmProviderInfo MiniMax = new()
    {
        Id = "minimax",
        ProtocolId = "openai",
        Name = "MiniMax",
        // 2026-08-04: the live baseUrl for M3/M2.7 (per the
        // platform's international surface) is api.minimax.chat.
        // api.minimax.io is an older host that some legacy
        // setups still hit — old settings files that carry it
        // continue to work via ProviderConfigurationValidator's
        // legacy-host rewrite path, but we don't surface it
        // here as a "default" anymore (new installs landed on
        // .io before the user even set a key, leading to the
        // 401 / "invalid api key (2049)" trap).
        DefaultBaseUrl = "https://api.minimax.chat/v1",
        DefaultModel = "MiniMax-M3",
        // 1_048_576 is the M3 ceiling (1M token context). The
        // old 200_000 default was the M2.7 limit leaked into
        // the flagship slot — for users who don't send long
        // docs this doesn't matter, but for the daily driver
        // pasting a whole repo into a prompt it was clipping
        // M3's advertised window to less than 20% of its real
        // capacity. The context ring in the status bar would
        // also mis-allocate against a 200K budget.
        DefaultContextLimit = 1_048_576,
        Models =
        [
            // M3 flagship. 1M context, native multimodal
            // (text+image+video), interleaved thinking, tools,
            // JSON mode, streaming, parallel tool calls, auto
            // prompt cache. The capabilities list reflects the
            // full surface; the CapabilityLabel is the
            // one-glance summary for the Settings dropdown.
            new LlmModelInfo
            {
                Id = "MiniMax-M3",
                DisplayName = "MiniMax-M3",
                ContextLimit = 1_048_576,
                CapabilityLabel = "1M · multimodal · thinking · tools",
                Capabilities = MiniMaxCapabilities,
                Parameters = MiniMaxM3Parameters
            },
            // M3-highspeed: same model identity, faster
            // inference. Useful for daily-driver loops where
            // latency matters more than the long-tail quality
            // edge cases. M3-highspeed and M3 are interchangeable
            // result-wise (per MiniMax docs) — the only
            // difference is the routing tier inside the
            // provider.
            new LlmModelInfo
            {
                Id = "MiniMax-M3-highspeed",
                DisplayName = "MiniMax-M3 (highspeed)",
                ContextLimit = 1_048_576,
                CapabilityLabel = "1M · multimodal · thinking · tools · 优先调度",
                Capabilities = MiniMaxCapabilities,
                Parameters = MiniMaxM3Parameters
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
