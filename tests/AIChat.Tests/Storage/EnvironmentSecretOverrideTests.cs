using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Storage.Json;

namespace AIChat.Tests.Storage;

// Verifies the dev / CI / shell-rc escape hatch: exporting `AICHAT_API_KEY`
// (or `AICHAT_PROVIDER_<NAME>_API_KEY`) makes the platform credential vault
// invisible at startup AND keeps settings.json's stored keychain reference
// intact, so the user can `unset` the env var to fall back to the keychain
// without losing their secret.
public sealed class EnvironmentSecretOverrideTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly TrackingSecretProtector _protector;
    private readonly JsonAppRepository _repo;
    private readonly string? _previousMainEnv;
    private readonly string? _previousProviderEnv;

    public EnvironmentSecretOverrideTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "AIChat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
        _protector = new TrackingSecretProtector();
        _repo = new JsonAppRepository(_dataDirectory, _protector);
        _previousMainEnv = Environment.GetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar);
        _previousProviderEnv = Environment.GetEnvironmentVariable(
            EnvironmentSecretOverride.ProviderKeyEnvVarPrefix + "MINIMAX" + EnvironmentSecretOverride.ProviderKeyEnvVarSuffix);
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, null);
        Environment.SetEnvironmentVariable(
            EnvironmentSecretOverride.ProviderKeyEnvVarPrefix + "MINIMAX" + EnvironmentSecretOverride.ProviderKeyEnvVarSuffix,
            null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, _previousMainEnv);
        Environment.SetEnvironmentVariable(
            EnvironmentSecretOverride.ProviderKeyEnvVarPrefix + "MINIMAX" + EnvironmentSecretOverride.ProviderKeyEnvVarSuffix,
            _previousProviderEnv);
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task MainKey_EnvOverride_ReturnsEnvironmentValue()
    {
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, "env-secret-123");
        SeedSettings(protectedApiKey: "settings-api-key", apiKeyProtection: "platform-keychain");

        var loaded = await _repo.LoadSettingsAsync();

        Assert.Equal("env-secret-123", loaded.ApiKey);
    }

    [Fact]
    public async Task MainKey_NoEnvOverride_FallsBackToKeychain()
    {
        SeedSettings(protectedApiKey: "settings-api-key", apiKeyProtection: "platform-keychain");
        _protector.MockSecret = "keychain-secret-456";

        var loaded = await _repo.LoadSettingsAsync();

        Assert.Equal("keychain-secret-456", loaded.ApiKey);
    }

    [Fact]
    public async Task ProviderKey_ProviderSpecificEnv_OverridesMainEnv()
    {
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, "main-env");
        Environment.SetEnvironmentVariable(
            EnvironmentSecretOverride.ProviderKeyEnvVarPrefix + "MINIMAX" + EnvironmentSecretOverride.ProviderKeyEnvVarSuffix,
            "provider-specific-env");
        SeedSettings(providerName: "MiniMax", providerProtectedApiKey: "provider-id-api-key");

        var loaded = await _repo.LoadSettingsAsync();

        var provider = Assert.Single(loaded.ConfiguredProviders);
        Assert.Equal("provider-specific-env", provider.ApiKey);
    }

    [Fact]
    public async Task ProviderKey_OnlyMainEnvSet_AppliesToProvider()
    {
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, "shared-env");
        SeedSettings(providerName: "MiniMax", providerProtectedApiKey: "provider-id-api-key");

        var loaded = await _repo.LoadSettingsAsync();

        var provider = Assert.Single(loaded.ConfiguredProviders);
        Assert.Equal("shared-env", provider.ApiKey);
    }

    [Fact]
    public async Task EnvOverride_LoadDoesNotCallUnprotectForOverriddenPurposes()
    {
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, "env-secret");
        SeedSettings(protectedApiKey: "settings-api-key", apiKeyProtection: "platform-keychain");

        await _repo.LoadSettingsAsync();

        Assert.Equal(0, _protector.UnprotectCalls);
    }

    [Fact]
    public async Task EnvOverride_SavePreservesKeychainReferenceInSettingsJson()
    {
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, "env-secret");
        SeedSettings(protectedApiKey: "settings-api-key", apiKeyProtection: "platform-keychain");

        var loaded = await _repo.LoadSettingsAsync();
        loaded.Model = "new-model-xyz";
        await _repo.SaveSettingsAsync(loaded);

        var json = await File.ReadAllTextAsync(Path.Combine(_dataDirectory, "settings.json"));
        Assert.Contains("\"protectedApiKey\": \"settings-api-key\"", json);
        Assert.Contains("\"apiKeyProtection\": \"platform-keychain\"", json);
        Assert.DoesNotContain("env-secret", json);
    }

    [Fact]
    public void NormalizeProviderName_MapsSpecialCharsToUnderscores()
    {
        Assert.Equal("MINI_MAX_PRO", EnvironmentSecretOverride.NormalizeProviderName("Mini Max-Pro"));
        Assert.Equal("OPENAI", EnvironmentSecretOverride.NormalizeProviderName("OpenAI"));
        Assert.Equal("", EnvironmentSecretOverride.NormalizeProviderName(""));
    }

    [Fact]
    public void NormalizeProviderName_IsExposedViaTryGetProviderKey()
    {
        // Provider named "Deep Seek-v2" (old hypothetical) should map to the
        // underscore-normalized env var name, not a literal "DEEPSEEKV2".
        Assert.Equal("AICHAT_PROVIDER_DEEP_SEEK_V2_API_KEY",
            EnvironmentSecretOverride.ProviderKeyEnvVarPrefix
            + EnvironmentSecretOverride.NormalizeProviderName("Deep Seek-v2")
            + EnvironmentSecretOverride.ProviderKeyEnvVarSuffix);
    }

    private void SeedSettings(
        string protectedApiKey = "settings-api-key",
        string apiKeyProtection = "platform-keychain",
        string providerName = "MiniMax",
        string providerProtectedApiKey = "provider-id-api-key")
    {
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            ProtocolId = "openai",
            ProviderName = "MiniMax",
            BaseUrl = "https://api.minimax.io/v1",
            ProtectedApiKey = protectedApiKey,
            ApiKeyProtection = apiKeyProtection,
            Model = "MiniMax-M3",
            ConfiguredProviders =
            [
                new ConfiguredLlmProvider
                {
                    Id = "provider-id",
                    TemplateId = "minimax",
                    ProtocolId = "openai",
                    Name = providerName,
                    BaseUrl = "https://api.minimax.io/v1",
                    SelectedModelId = "MiniMax-M3",
                    ProtectedApiKey = providerProtectedApiKey,
                    ApiKeyProtection = "platform-keychain"
                }
            ]
        };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        File.WriteAllText(Path.Combine(_dataDirectory, "settings.json"), json);
    }

    private sealed class TrackingSecretProtector : ISecretProtector
    {
        public int UnprotectCalls;
        public int ProtectCalls;
        public string MockSecret = "keychain-secret";

        public ProtectedSecret Protect(string secret, string purpose)
        {
            ProtectCalls++;
            return new ProtectedSecret(purpose, "platform-keychain");
        }

        public string Unprotect(string protectedValue, string protection, string purpose)
        {
            UnprotectCalls++;
            return MockSecret;
        }

        public void Delete(string purpose)
        {
        }
    }
}
