using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Configuration;

public sealed class ProviderSettingsServiceTests
{
    [Fact]
    public void Normalize_MigratesLegacyApiKeyToConfiguredProvider()
    {
        var settings = new AppSettings
        {
            ProviderId = "deepseek",
            ApiKey = "legacy-key",
            Model = "deepseek-v4-flash",
            ConfiguredProviders = []
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        var configured = Assert.Single(settings.ConfiguredProviders);
        Assert.Equal("deepseek", configured.TemplateId);
        Assert.Equal("legacy-key", configured.ApiKey);
        Assert.Equal(configured.Id, settings.ActiveConfiguredProviderId);
        Assert.Equal("deepseek-v4-flash", settings.Model);
    }

    [Fact]
    public void Normalize_DeduplicatesConfiguredProvidersAndKeepsActiveDuplicate()
    {
        var first = new ConfiguredLlmProvider
        {
            Id = "first",
            TemplateId = "deepseek",
            ApiKey = "same-key",
            SelectedModelId = "deepseek-chat"
        };
        var active = new ConfiguredLlmProvider
        {
            Id = "active",
            TemplateId = "deepseek",
            ApiKey = "same-key",
            SelectedModelId = "deepseek-v4-pro"
        };
        var settings = new AppSettings
        {
            ProviderId = "deepseek",
            ActiveConfiguredProviderId = active.Id,
            ConfiguredProviders = [first, active]
        };

        ProviderSettingsService.Normalize(settings, defaultTemperature: 0.3);

        var configured = Assert.Single(settings.ConfiguredProviders);
        Assert.Equal("active", configured.Id);
        Assert.Equal("active", settings.ActiveConfiguredProviderId);
        Assert.Equal("deepseek-v4-pro", settings.Model);
    }

    [Fact]
    public void AddConfiguredProvider_ExistingKeySwitchesToExistingProvider()
    {
        var existing = new ConfiguredLlmProvider
        {
            Id = "existing",
            TemplateId = "minimax",
            ApiKey = "key-1",
            SelectedModelId = "MiniMax-M2"
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
        Assert.Equal("MiniMax-M2", settings.Model);
    }

    [Fact]
    public void NormalizeModelParameterValues_DropsUnknownAndResetsInvalidOption()
    {
        var values = ProviderSettingsService.NormalizeModelParameterValues(
            "deepseek",
            "deepseek-v4-pro",
            new Dictionary<string, string>
            {
                ["deepseek.thinking"] = "nonsense",
                ["unknown"] = "keep-me"
            });

        Assert.DoesNotContain("unknown", values.Keys);
        Assert.Equal("", values["deepseek.thinking"]);
        Assert.True(values.ContainsKey("deepseek.reasoning_effort"));
        Assert.True(values.ContainsKey("deepseek.response_format"));
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
                    TemplateId = "deepseek",
                    SelectedModelId = "deepseek-v4-pro"
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
                    TemplateId = "deepseek",
                    ApiKey = "key-1",
                    SelectedModelId = "deepseek-v4-pro",
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
            ProviderId = "deepseek",
            ProtocolId = "openai",
            ProviderName = "DeepSeek",
            BaseUrl = "not a url",
            ApiKey = "",
            Model = "deepseek-v4-pro",
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            ModelContextLimit = 128_000
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == "provider.api_key");
        Assert.Contains(result.Errors, issue => issue.Code == "provider.base_url");
    }

    [Fact]
    public void ValidateEffectiveSettings_RejectsUnsupportedModelForTools()
    {
        var result = ProviderConfigurationValidator.ValidateEffectiveSettings(new AppSettings
        {
            ProviderId = "deepseek",
            ProtocolId = "openai",
            ProviderName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com",
            ApiKey = "key",
            Model = "missing-model",
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            ModelContextLimit = 128_000
        }, requireTools: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == "provider.model");
    }

    [Fact]
    public void ValidateEffectiveSettings_WarnsForUnknownModelParameter()
    {
        var result = ProviderConfigurationValidator.ValidateEffectiveSettings(new AppSettings
        {
            ProviderId = "deepseek",
            ProtocolId = "openai",
            ProviderName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com",
            ApiKey = "key",
            Model = "deepseek-v4-pro",
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            ModelContextLimit = 128_000,
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
                    TemplateId = "deepseek",
                    ProtocolId = "openai",
                    Name = "DeepSeek",
                    BaseUrl = "https://proxy.example.com/v1",
                    ApiKey = "key",
                    SelectedModelId = "deepseek-v4-pro"
                }
            ]
        };

        var effective = ProviderSettingsService.CreateEffectiveSettings(settings, defaultTemperature: 0.3);

        Assert.NotNull(effective);
        Assert.Equal("https://proxy.example.com/v1", effective!.BaseUrl);
    }
}
