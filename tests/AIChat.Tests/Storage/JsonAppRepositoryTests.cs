using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Domain.Projects;
using AIChat.Storage.Json;

namespace AIChat.Tests.Storage;

public sealed class JsonAppRepositoryTests : IDisposable
{
    private readonly string _dataDirectory;

    public JsonAppRepositoryTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "AIChat.Tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
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
        settings.Model = "mimo-v2.5-pro";
        await repo.SaveSettingsAsync(settings);
    }

    [Fact]
    public async Task SaveProjects_DoesNotCorruptOnConcurrentWrites()
    {
        var repo = new JsonAppRepository(_dataDirectory);
        var projects = await repo.LoadProjectsAsync();

        // Fire multiple concurrent saves — should not throw or corrupt
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            repo.SaveProjectsAsync(projects));

        await Task.WhenAll(tasks);

        // Verify file is still valid JSON
        var loaded = await repo.LoadProjectsAsync();
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
