using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Domain.Projects;
using AIChat.Storage.Json;

namespace AIChat.Tests.Storage;

public sealed class JsonAppRepositoryTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string? _previousIsolatedRoot;

    public JsonAppRepositoryTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "AIChat.Tests", Guid.NewGuid().ToString("N"));
        // 2026-08-03: redirect AppRuntimeProfile.DataDirectory at
        // the per-test temp path so the EnvironmentSecretOverride
        // layer reads the .env we author (or its absence) inside
        // this test's throwaway directory, not from the user's
        // real ~/Library/Application Support/AIChat/.env on the
        // test machine. Without this, a daily-driver user with
        // AICHAT_API_KEY configured would see every test of this
        // file silently pick up the production secret and
        // "verify" the wrong value.
        _previousIsolatedRoot = Environment.GetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT");
        Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", _dataDirectory);
    }

    public void Dispose()
    {
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
            // Best-effort cleanup; temp directories should not hide test failures.
        }
    }

    [Fact]
    public async Task SaveAndLoadSettings_RoundTrips()
    {
        var repo = new JsonAppRepository(_dataDirectory);
        var settings = await repo.LoadSettingsAsync();

        // Modify a field and save
        settings.Model = "test-model-xyz";
        await repo.SaveSettingsAsync(settings);

        var loaded = await repo.LoadSettingsAsync();
        Assert.Equal("test-model-xyz", loaded.Model);

        // Restore original
        settings.Model = "MiniMax-M3";
        await repo.SaveSettingsAsync(settings);
    }

    [Fact]
    public async Task SaveWorkspaces_DoesNotCorruptOnConcurrentWrites()
    {
        // Wave 3: rewritten to use the v1 API (LoadWorkspacesAsync /
        // SaveWorkspacesAsync). The v0 SaveProjectsAsync was deleted
        // in this wave; the concurrent-write invariant it used to
        // lock down now lives on the v1 path.
        var repo = new JsonAppRepository(_dataDirectory);
        var workspaces = await repo.LoadWorkspacesAsync();
        if (workspaces.Count == 0)
        {
            // First launch: seed one workspace so the concurrent
            // writes have something to flush.
            var folderId = Guid.NewGuid().ToString("N");
            workspaces = [new WorkspaceProject
            {
                Id = folderId,
                Name = "seed",
                Folders = [new WorkspaceFolder { Id = folderId, Path = _dataDirectory }],
                PrimaryFolderId = folderId,
            }];
        }

        // Fire multiple concurrent saves — should not throw or corrupt
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            repo.SaveWorkspacesAsync(workspaces));

        await Task.WhenAll(tasks);

        // Verify file is still valid JSON
        var loaded = await repo.LoadWorkspacesAsync();
        Assert.NotNull(loaded);
        Assert.True(loaded.Count > 0);
    }

    [Fact]
    public async Task AtomicWrite_DoesNotLeaveTempFile()
    {
        var repo = new JsonAppRepository(_dataDirectory);
        var settings = await repo.LoadSettingsAsync();
        await repo.SaveSettingsAsync(settings);

        // Check no .tmp file remains
        var tmpFiles = Directory.GetFiles(_dataDirectory, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task SaveSettings_ProtectsApiKeysOnDiskAndRestoresOnLoad()
    {
        // Windows uses DPAPI to protect API keys on disk. On macOS and Linux the
        // current implementation falls back to "plain" mode (see
        // ProtectedSettingsSerializer.ProtectSecret), so this disk-content
        // assertion is Windows-only. Tracking the macOS/Linux protection gap
        // separately.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repo = new JsonAppRepository(_dataDirectory);
        var settings = await repo.LoadSettingsAsync();
        settings.ApiKey = "legacy-secret-key";
        settings.ConfiguredProviders =
        [
            new ConfiguredLlmProvider
            {
                Name = "Test",
                ApiKey = "provider-secret-key"
            }
        ];

        await repo.SaveSettingsAsync(settings);

        var json = await File.ReadAllTextAsync(Path.Combine(_dataDirectory, "settings.json"));
        Assert.DoesNotContain("legacy-secret-key", json);
        Assert.DoesNotContain("provider-secret-key", json);
        Assert.Contains("protectedApiKey", json);

        var loaded = await repo.LoadSettingsAsync();
        Assert.Equal("legacy-secret-key", loaded.ApiKey);
        Assert.Equal("provider-secret-key", Assert.Single(loaded.ConfiguredProviders).ApiKey);
    }

    [Fact]
    public async Task LoadSettings_RestoresLegacyPlainTextApiKeys()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, "settings.json"),
            """
            {
              "providerId": "openai",
              "protocolId": "openai",
              "providerName": "OpenAI",
              "baseUrl": "https://api.openai.com/v1",
              "apiKey": "legacy-secret-key",
              "model": "gpt-test",
              "configuredProviders": [
                {
                  "id": "provider-1",
                  "templateId": "openai",
                  "protocolId": "openai",
                  "name": "OpenAI",
                  "baseUrl": "https://api.openai.com/v1",
                  "apiKey": "provider-secret-key",
                  "selectedModelId": "gpt-test"
                }
              ]
            }
            """);

        var loaded = await new JsonAppRepository(_dataDirectory).LoadSettingsAsync();

        Assert.Equal("legacy-secret-key", loaded.ApiKey);
        Assert.Equal("provider-secret-key", Assert.Single(loaded.ConfiguredProviders).ApiKey);
    }

    [Fact]
    public async Task LoadSettings_IgnoresCorruptProtectedApiKey()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, "settings.json"),
            """
            {
              "providerId": "openai",
              "protocolId": "openai",
              "providerName": "OpenAI",
              "baseUrl": "https://api.openai.com/v1",
              "protectedApiKey": "not-base64",
              "apiKeyProtection": "dpapi-current-user",
              "model": "gpt-test"
            }
            """);

        var loaded = await new JsonAppRepository(_dataDirectory).LoadSettingsAsync();

        Assert.Equal("", loaded.ApiKey);
    }
}
