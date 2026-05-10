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
}
