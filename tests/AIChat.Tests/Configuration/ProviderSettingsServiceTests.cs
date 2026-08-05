using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Configuration;

// 2026-08-02: the catalog is now MiniMax-only. Tests that previously
// stood up DeepSeek-shaped settings (provider id, model id, base url)
// are rewritten against MiniMax / MiniMax-M3 — the only ship target.
// The contracts they pin (legacy-key migration, dedup, normalization,
// vision override, validator semantics, custom model id allowance) are
// all provider-agnostic; the only change is the concrete id strings.
public sealed class ProviderSettingsServiceTests
{
    [Fact]
    public void Normalize_MigratesLegacyApiKeyToConfiguredProvider()
    {
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            ApiKey = "legacy-key",
            Model = "MiniMax-M3",
            ConfiguredProviders = []
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        var configured = Assert.Single(settings.ConfiguredProviders);
        Assert.Equal("minimax", configured.TemplateId);
        Assert.Equal("legacy-key", configured.ApiKey);
        Assert.Equal(configured.Id, settings.ActiveConfiguredProviderId);
        Assert.Equal("MiniMax-M3", settings.Model);
    }

    // 2026-08-05: catalog surface lock. Two shipping
    // models (M3 / M3-highspeed), each 1M context +
    // multimodal + thinking. M2.7 / M2.7-highspeed
    // dropped 2026-08-05 — MiniMax unified the Coding
    // Plan + Token Plan billing surfaces so sk-cp-…
    // keys now authenticate against M3 too. A future
    // change that adds / removes a model — or bumps
    // the context limit on the flagship — breaks
    // here rather than as a silent feature drop in
    // production.
    [Fact]
    public void Catalog_ListsM3AndM3Highspeed()
    {
        var ids = ChatProviderCatalog.MiniMax.Models.Select(m => m.Id).ToArray();

        Assert.Contains("MiniMax-M3", ids);
        Assert.Contains("MiniMax-M3-highspeed", ids);
        Assert.DoesNotContain("MiniMax-M2.7", ids);
        Assert.DoesNotContain("MiniMax-M2.7-highspeed", ids);
    }

    // 2026-08-04: M3 ships a thinking-mode switch
    // (`enabled` / `adaptive` / `disabled`) — the knob
    // the daily driver wants when they say "思考模式
    // 的开关". This test pins the parameter shape so a
    // future refactor that drops / renames it shows up
    // here (vs. silently shipping a Settings modal that
    // doesn't expose the switch).
    [Fact]
    public void Catalog_M3ExposesThinkingModeSwitch()
    {
        var m3 = ChatProviderCatalog.MiniMax.Models.Single(m => m.Id == "MiniMax-M3");

        var thinking = Assert.Single(m3.Parameters, p => p.Id == "minimax.thinking");
        var values = thinking.Options.Select(o => o.Value).ToArray();

        // The empty-string "" option is the "默认 (adaptive)"
        // catalog row — emits nothing on the wire, lets the
        // platform pick its default. The other three are
        // the explicit user-overridable values per the M3
        // README.
        Assert.Contains("", values);
        Assert.Contains("enabled", values);
        Assert.Contains("adaptive", values);
        Assert.Contains("disabled", values);
    }

    // 2026-08-04: structured JSON output (response_format)
    // is wired as a per-model dropdown for M3. M2.7
    // inherits the same knob (kept in MiniMaxM27Parameters
    // for the freeform-id path) — both model lines
    // support the OpenAI-compatible response_format
    // contract.
    [Fact]
    public void Catalog_M3ExposesJsonMode()
    {
        var m3 = ChatProviderCatalog.MiniMax.Models.Single(m => m.Id == "MiniMax-M3");

        var json = Assert.Single(m3.Parameters, p => p.Id == "response_format");
        var values = json.Options.Select(o => o.Value).ToArray();

        Assert.Contains("", values);
        Assert.Contains("json_object", values);
    }

    // 2026-08-05: M2.7 dropped from the catalog dropdown
    // (Coding Plan keys now auth M3). A user with
    // `model: MiniMax-M2.7` already in their settings.json
    // still resolves through ResolveModel's
    // "non-empty user-typed id" path — but the result
    // is a synthetic LlmModelInfo carrying the provider
    // defaults (1M context, ToolCapable, empty Parameters)
    // rather than the M2.7-specific 200K / no-vision
    // shape. The user-typed-id branch deliberately
    // doesn't special-case legacy model ids — it'd
    // re-introduce the "M2.7 is special" coupling that
    // we just dropped. The OpenAICompatibleChatProvider's
    // switch handles parameters by id, not by model, so
    // a M2.7 user who has `top_p=0.5` in their
    // settings.json still gets that knob on the wire
    // (and M2.7's API honors it). Pin the synthetic
    // shape so a future ResolveModel refactor that
    // re-introduces M2.7 special-casing shows up here.
    [Fact]
    public void ResolveModel_TypedM27Id_ReturnsSyntheticWithProviderDefaults()
    {
        var resolved = ChatProviderCatalog.ResolveModel("minimax", "MiniMax-M2.7");

        Assert.Equal("MiniMax-M2.7", resolved.Id);
        // Synthetic falls back to provider.DefaultContextLimit
        // (1M, the M3 limit) — not the M2.7 historical
        // 200K. Users with old M2.7 settings should
        // re-pick M3 from the dropdown; the Settings
        // modal now shows the M3-only knob set.
        Assert.Equal(1_048_576, resolved.ContextLimit);
        // Empty parameters list: the OpenAI provider
        // reads settings.ModelParameters by id, so
        // any saved M2.7-era knob values (top_p etc.)
        // still take effect on the wire.
        Assert.Empty(resolved.Parameters);
    }

    [Fact]
    public void Catalog_M3ContextLimitIsOneMillion()
    {
        // The 2026-08-04 catalog change set the flagship
        // default context to 1_048_576. The previous
        // 200_000 default was a 1.0-era leak from the
        // M2 line and was the root cause of the "status
        // bar says 0% / 5% while the agent was actually
        // near the limit" complaint. Pin the new value
        // so a future revert breaks the build, not the
        // user's context ring.
        Assert.Equal(1_048_576, ChatProviderCatalog.MiniMax.DefaultContextLimit);

        var m3 = ChatProviderCatalog.MiniMax.Models.Single(m => m.Id == "MiniMax-M3");
        Assert.Equal(1_048_576, m3.ContextLimit);
        Assert.True(m3.Capabilities.SupportsVision,
            "M3 is native multimodal — SupportsVision must stay on for the image-paste pipeline to work");
    }

    [Fact]
    public void Catalog_DefaultBaseUrlIsMinimaxChat()
    {
        // The pre-1.0.0 catalog defaulted to api.minimax.io,
        // which the live platform now returns 401 against
        // (curl probe — invalid api key (2049)). The new
        // default is api.minimax.chat, which is the host
        // the current M3 / M2.7 keys are minted on. Old
        // settings files carrying .io are auto-rewritten
        // by ProviderSettingsService.Normalize via the
        // LegacyProviderHosts list.
        Assert.Equal("https://api.minimax.chat/v1", ChatProviderCatalog.MiniMax.DefaultBaseUrl);
    }

    [Fact]
    public void Normalize_RewritesLegacyIoBaseUrlToChat()
    {
        // A user with a 0.5 settings file whose BaseUrl
        // was written before the platform moved to .chat
        // would otherwise hit HTTP 401 on every send. The
        // Normalize path detects the legacy host and
        // rewrites it to the catalog default — same
        // behavior as the pre-1.0 anthropic / deepseek /
        // xiaomimimo migration.
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            ApiKey = "key",
            Model = "MiniMax-M3",
            BaseUrl = "https://api.minimax.io/v1",
            ConfiguredProviders = []
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        Assert.Equal("https://api.minimax.chat/v1", settings.BaseUrl);
    }

    [Fact]
    public void Normalize_DeduplicatesConfiguredProvidersAndKeepsActiveDuplicate()
    {
        var first = new ConfiguredLlmProvider
        {
            Id = "first",
            TemplateId = "minimax",
            ApiKey = "same-key",
            SelectedModelId = "MiniMax-M3"
        };
        var active = new ConfiguredLlmProvider
        {
            Id = "active",
            TemplateId = "minimax",
            ApiKey = "same-key",
            SelectedModelId = "MiniMax-M3"
        };
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            ActiveConfiguredProviderId = active.Id,
            ConfiguredProviders = [first, active]
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        var configured = Assert.Single(settings.ConfiguredProviders);
        Assert.Equal("active", configured.Id);
        Assert.Equal("active", settings.ActiveConfiguredProviderId);
        Assert.Equal("MiniMax-M3", settings.Model);
    }

    [Fact]
    public void Normalize_PreservesUserBaseUrl_WhenSelfHosted()
    {
        // Regression guard for the 2026-08-02 fix: a user with a
        // self-hosted proxy (e.g. a private gateway at
        // https://proxy.example.com/v1) used to see their BaseUrl
        // silently overwritten by Normalize on every startup,
        // shipping the next message to the wrong endpoint. The
        // fix: keep a valid http(s) BaseUrl as-is; only fill the
        // provider default when the stored value is missing or
        // malformed.
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            BaseUrl = "https://proxy.example.com/v1",
            Model = "MiniMax-M3"
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        Assert.Equal("https://proxy.example.com/v1", settings.BaseUrl);
    }

    [Theory]
    [InlineData("https://api.anthropic.com/v1")]
    [InlineData("https://api.deepseek.com/v1")]
    [InlineData("https://token-plan-cn.xiaomimimo.com/v1")]
    [InlineData("https://api.xiaomimimo.com/v1")]
    public void Normalize_RewritesLegacyProviderHost_ToCatalogDefault(string legacyBaseUrl)
    {
        // Regression guard for the 1.0 upgrade path: a 0.5 user with a
        // stored BaseUrl pointing at a provider that was removed in
        // the Provider prune would otherwise see a silent 401 / 404
        // on the next message. Force-rewrite to the catalog default.
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            BaseUrl = legacyBaseUrl,
            Model = "MiniMax-M3"
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultBaseUrl, settings.BaseUrl);
    }

    [Fact]
    public void Normalize_KeepsCustomBaseUrl_WhenHostIsNotInLegacyList()
    {
        // Self-hosted MiniMax-style proxy at a non-legacy host
        // (e.g. corporate gateway) must survive Normalize. Only the
        // hard-coded legacy hosts trigger the rewrite.
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            BaseUrl = "https://proxy.example.com/v1",
            Model = "MiniMax-M3"
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        Assert.Equal("https://proxy.example.com/v1", settings.BaseUrl);
    }

    [Fact]
    public void Normalize_RewritesLegacyProviderHost_InConfiguredProviders()
    {
        // Same rewrite applies to per-provider entries: a 0.5 user
        // with a stored DeepSeek entry in `configuredProviders` must
        // not silently ship traffic to api.deepseek.com after the
        // catalog shrinks to MiniMax-only.
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            Model = "MiniMax-M3",
            ConfiguredProviders =
            [
                new ConfiguredLlmProvider
                {
                    Id = "legacy-1",
                    TemplateId = "minimax",
                    ApiKey = "kept-key",
                    BaseUrl = "https://api.deepseek.com/v1",
                    SelectedModelId = "MiniMax-M3"
                }
            ]
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        var configured = Assert.Single(settings.ConfiguredProviders);
        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultBaseUrl, configured.BaseUrl);
        Assert.Equal("kept-key", configured.ApiKey);
    }

    [Fact]
    public void Normalize_FillsProviderDefault_WhenBaseUrlIsBlank()
    {
        // Brand-new install path: BaseUrl is empty by
        // construction, so the provider's default fills in.
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            BaseUrl = "",
            Model = "MiniMax-M3"
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultBaseUrl, settings.BaseUrl);
    }

    [Fact]
    public void Normalize_FillsProviderDefault_WhenBaseUrlIsMalformed()
    {
        // Stale / hand-typed BaseUrl that's not a valid http(s)
        // URL gets replaced with the provider default rather than
        // being passed through to the chat completion client (which
        // would throw at request time).
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            BaseUrl = "not a url",
            Model = "MiniMax-M3"
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultBaseUrl, settings.BaseUrl);
    }

    [Fact]
    public void AddConfiguredProvider_ExistingKeySwitchesToExistingProvider()
    {
        var existing = new ConfiguredLlmProvider
        {
            Id = "existing",
            TemplateId = "minimax",
            ApiKey = "key-1",
            SelectedModelId = "MiniMax-M3"
        };
        var settings = new AppSettings
        {
            ConfiguredProviders = [existing]
        };

        var result = ProviderSettingsService.AddConfiguredProvider(settings, "minimax", "key-1");

        Assert.True(result.AlreadyExisted);
        Assert.Same(existing, result.Provider);
        Assert.Single(settings.ConfiguredProviders);
        Assert.Equal("existing", settings.ActiveConfiguredProviderId);
        Assert.Equal("MiniMax-M3", settings.Model);
    }

    [Fact]
    public void AddConfiguredProvider_DefaultsToMiniMaxTemplate()
    {
        // The catalog is now single-provider. AddConfiguredProvider
        // with the catalog's only id lands on MiniMax with the
        // current default model.
        var settings = new AppSettings();

        var result = ProviderSettingsService.AddConfiguredProvider(settings, "minimax", "key-1");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("minimax", result.Provider.TemplateId);
        Assert.Equal("openai", result.Provider.ProtocolId);
        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultModel, result.Provider.SelectedModelId);
        Assert.Equal(result.Provider.Id, settings.ActiveConfiguredProviderId);
    }

    [Fact]
    public void NormalizeModelParameterValues_DropsUnknownAndResetsInvalidOption()
    {
        // The MiniMax catalog exposes a single parameter
        // (`minimax.reasoning_split`). Bogus values get reset to
        // the empty default; unknown keys get dropped.
        var values = ProviderSettingsService.NormalizeModelParameterValues(
            "minimax",
            "MiniMax-M3",
            new Dictionary<string, string>
            {
                ["minimax.reasoning_split"] = "nonsense",
                ["unknown"] = "keep-me"
            });

        Assert.DoesNotContain("unknown", values.Keys);
        Assert.Equal("", values["minimax.reasoning_split"]);
    }

    [Fact]
    public void CreateEffectiveSettings_ReturnsNullWhenActiveProviderHasNoApiKey()
    {
        var settings = new AppSettings
        {
            ActiveConfiguredProviderId = "no-key",
            ConfiguredProviders =
            [
                new ConfiguredLlmProvider
                {
                    Id = "no-key",
                    TemplateId = "minimax",
                    SelectedModelId = "MiniMax-M3"
                }
            ]
        };

        var effective = ProviderSettingsService.CreateEffectiveSettings(settings, defaultTemperature: 0.3);

        Assert.Null(effective);
    }

    [Fact]
    public void CreateEffectiveSettings_CarriesVisionOverride()
    {
        var settings = new AppSettings
        {
            ActiveConfiguredProviderId = "vision-provider",
            ConfiguredProviders =
            [
                new ConfiguredLlmProvider
                {
                    Id = "vision-provider",
                    TemplateId = "minimax",
                    ApiKey = "key-1",
                    SelectedModelId = "MiniMax-M3",
                    SupportsVisionOverride = true
                }
            ]
        };

        var effective = ProviderSettingsService.CreateEffectiveSettings(settings, defaultTemperature: 0.3);

        Assert.NotNull(effective);
        Assert.True(effective!.ModelSupportsVision);
    }

    [Fact]
    public void ValidateEffectiveSettings_ReturnsErrorsForMissingApiKeyAndBadBaseUrl()
    {
        var result = ProviderConfigurationValidator.ValidateEffectiveSettings(new AppSettings
        {
            ProviderId = "minimax",
            ProtocolId = "openai",
            ProviderName = "MiniMax",
            BaseUrl = "not a url",
            ApiKey = "",
            Model = "MiniMax-M3",
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            ModelContextLimit = 200_000
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == "provider.api_key");
        Assert.Contains(result.Errors, issue => issue.Code == "provider.base_url");
    }

    [Fact]
    public void ValidateEffectiveSettings_RejectsBadTemperature()
    {
        // After the 2026-08-02 catalog prune, MiniMax accepts any
        // model id (it's an OpenAI-compatible endpoint and users
        // may run a self-hosted proxy / private deployment). The
        // "missing-model gets rejected" test that used to pin the
        // catalog-as-gate contract was retired with it; this test
        // keeps the validation suite honest by exercising a
        // different error path (out-of-range temperature) that
        // doesn't depend on catalog model membership.
        var result = ProviderConfigurationValidator.ValidateEffectiveSettings(new AppSettings
        {
            ProviderId = "minimax",
            ProtocolId = "openai",
            ProviderName = "MiniMax",
            BaseUrl = "https://api.minimax.io/v1",
            ApiKey = "key",
            Model = "MiniMax-M3",
            Temperature = 9.9,
            MaxOutputTokens = 4096,
            ModelContextLimit = 200_000
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == "provider.temperature");
    }

    [Fact]
    public void ValidateEffectiveSettings_AllowsCustomMiniMaxModel()
    {
        // MiniMax exposes an OpenAI-compatible endpoint, so users
        // running against a self-hosted proxy / private deployment
        // can type any model id and have it bind. The catalog's
        // model list is a defaults source, not a gate — same
        // contract the previous "AllowsCustomOpenAICompatibleModel"
        // test pinned, just for the only remaining provider.
        var result = ProviderConfigurationValidator.ValidateEffectiveSettings(new AppSettings
        {
            ProviderId = "minimax",
            ProtocolId = "openai",
            ProviderName = "MiniMax",
            BaseUrl = "https://gateway.example.com/v1",
            ApiKey = "key",
            Model = "private-cluster-2026-08",
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            ModelContextLimit = 200_000
        }, requireTools: true);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, issue => issue.Code == "provider.model");
    }

    [Fact]
    public void ValidateEffectiveSettings_WarnsForUnknownModelParameter()
    {
        var result = ProviderConfigurationValidator.ValidateEffectiveSettings(new AppSettings
        {
            ProviderId = "minimax",
            ProtocolId = "openai",
            ProviderName = "MiniMax",
            BaseUrl = "https://api.minimax.io/v1",
            ApiKey = "key",
            Model = "MiniMax-M3",
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            ModelContextLimit = 200_000,
            ModelParameters = new Dictionary<string, string>
            {
                ["unknown"] = "value"
            }
        });

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, issue => issue.Code == "provider.parameter.unknown");
    }

    [Fact]
    public void CreateEffectiveSettings_PreservesCustomBaseUrl()
    {
        var settings = new AppSettings
        {
            ActiveConfiguredProviderId = "custom",
            ConfiguredProviders =
            [
                new ConfiguredLlmProvider
                {
                    Id = "custom",
                    TemplateId = "minimax",
                    ProtocolId = "openai",
                    Name = "MiniMax",
                    BaseUrl = "https://proxy.example.com/v1",
                    ApiKey = "key",
                    SelectedModelId = "MiniMax-M3"
                }
            ]
        };

        var effective = ProviderSettingsService.CreateEffectiveSettings(settings, defaultTemperature: 0.3);

        Assert.NotNull(effective);
        Assert.Equal("https://proxy.example.com/v1", effective!.BaseUrl);
    }
}
