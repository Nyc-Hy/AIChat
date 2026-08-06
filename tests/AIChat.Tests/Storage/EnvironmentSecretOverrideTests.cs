using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Storage.Json;

namespace AIChat.Tests.Storage;

// Verifies the dev / CI / shell-rc escape hatch: exporting `AICHAT_API_KEY`
// (or `AICHAT_PROVIDER_<NAME>_API_KEY`) makes the platform credential vault
// invisible at startup AND keeps settings.json's stored keychain reference
// intact, so the user can `unset` the env var to fall back to the keychain
// without losing their secret.
[Collection(ProcessEnvMutatingCollection.Name)]
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
        // Redirect AppRuntimeProfile.DataDirectory at the per-test
        // temp path so dotenv lookup lands in our throwaway
        // directory. The previous value (production user's real
        // path) is restored in Dispose.
        _previousIsolatedRoot = Environment.GetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT");
        Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", _dataDirectory);
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
        Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", _previousIsolatedRoot);
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

    private readonly string? _previousIsolatedRoot;

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
    public void ApiKeyFile_EnvVarPath_OverridesMainKey()
    {
        // 2026-08-03: macOS GUI apps do NOT inherit shell rc, so the
        // env-var-only path silently fails when the app is launched
        // from Finder / Dock / Spotlight. AICHAT_API_KEY_FILE points
        // at a single-purpose secret file (CI-friendly, GUI-friendly,
        // no shell dependency) and its contents become the main key.
        var keyFile = Path.Combine(_dataDirectory, "main-key");
        File.WriteAllText(keyFile, "file-secret-789\n");
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.ApiKeyFileEnvVar, keyFile);
        try
        {
            var ok = EnvironmentSecretOverride.TryGetMainKey(out var value);
            Assert.True(ok);
            Assert.Equal("file-secret-789", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentSecretOverride.ApiKeyFileEnvVar, null);
        }
    }

    [Fact]
    public void DotEnv_File_OverridesMainKey_WhenNoEnvVarOrKeyFile()
    {
        // The data-directory `.env` is the catch-all that works for
        // every launcher (shell, Finder, Dock, Spotlight). Bare-line
        // format: the whole line is the main key. Users with a single
        // key do not have to remember dotenv syntax.
        var dotEnvPath = Path.Combine(_dataDirectory, ".env");
        File.WriteAllText(dotEnvPath, "dotenv-secret-xyz\n");
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, null);
        try
        {
            var ok = EnvironmentSecretOverride.TryGetMainKey(out var value);
            Assert.True(ok);
            Assert.Equal("dotenv-secret-xyz", value);
        }
        finally
        {
            try { File.Delete(dotEnvPath); } catch { }
        }
    }

    [Fact]
    public void DotEnv_File_KeyEqualsValue_OverridesProviderKey()
    {
        var dotEnvPath = Path.Combine(_dataDirectory, ".env");
        File.WriteAllText(dotEnvPath, "AICHAT_PROVIDER_MINIMAX_API_KEY=dotenv-provider-key\n");
        Environment.SetEnvironmentVariable(
            EnvironmentSecretOverride.ProviderKeyEnvVarPrefix + "MINIMAX" + EnvironmentSecretOverride.ProviderKeyEnvVarSuffix,
            null);
        try
        {
            var ok = EnvironmentSecretOverride.TryGetProviderKey("MiniMax", out var value);
            Assert.True(ok);
            Assert.Equal("dotenv-provider-key", value);
        }
        finally
        {
            try { File.Delete(dotEnvPath); } catch { }
        }
    }

    [Fact]
    public void DotEnv_File_CommentsAndBlankLines_AreIgnored()
    {
        var dotEnvPath = Path.Combine(_dataDirectory, ".env");
        File.WriteAllText(dotEnvPath, """
            # This is a comment
            AICHAT_API_KEY=real-key-123

            # Another comment
            AICHAT_PROVIDER_MINIMAX_API_KEY=provider-key-456
            """);
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, null);
        try
        {
            Assert.True(EnvironmentSecretOverride.TryGetMainKey(out var main));
            Assert.Equal("real-key-123", main);
            Assert.True(EnvironmentSecretOverride.TryGetProviderKey("MiniMax", out var prov));
            Assert.Equal("provider-key-456", prov);
        }
        finally
        {
            try { File.Delete(dotEnvPath); } catch { }
        }
    }

    [Fact]
    public void DotEnv_File_QuotedValue_StripsSurroundingQuotes()
    {
        var dotEnvPath = Path.Combine(_dataDirectory, ".env");
        File.WriteAllText(dotEnvPath, "AICHAT_API_KEY=\"quoted-secret\"\n");
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.MainKeyEnvVar, null);
        try
        {
            Assert.True(EnvironmentSecretOverride.TryGetMainKey(out var value));
            Assert.Equal("quoted-secret", value);
        }
        finally
        {
            try { File.Delete(dotEnvPath); } catch { }
        }
    }

    [Fact]
    public void ApiKeyFile_TakesPrecedenceOverDotEnv()
    {
        // The precedence order is env > key-file > dotenv. When two
        // sources are both present, the higher-priority one wins so
        // an operator can override the on-disk value without
        // touching the file.
        var keyFile = Path.Combine(_dataDirectory, "main-key");
        File.WriteAllText(keyFile, "key-file-value\n");
        var dotEnvPath = Path.Combine(_dataDirectory, ".env");
        File.WriteAllText(dotEnvPath, "AICHAT_API_KEY=dotenv-value\n");
        Environment.SetEnvironmentVariable(EnvironmentSecretOverride.ApiKeyFileEnvVar, keyFile);
        try
        {
            Assert.True(EnvironmentSecretOverride.TryGetMainKey(out var value));
            Assert.Equal("key-file-value", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentSecretOverride.ApiKeyFileEnvVar, null);
            try { File.Delete(dotEnvPath); } catch { }
        }
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
