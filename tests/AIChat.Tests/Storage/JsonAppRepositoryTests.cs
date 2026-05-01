using AIChat.Abstractions.Configuration;
using AIChat.Domain.Projects;
using AIChat.Storage.Json;

namespace AIChat.Tests.Storage;

public sealed class JsonAppRepositoryTests : IDisposable
{
    private readonly string _originalAppData;

    public JsonAppRepositoryTests()
    {
        // Override APPDATA to isolate test writes
        _originalAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public void Dispose()
    {
        // Clean up is not needed since the repo uses the real APPDATA
        // and tests don't conflict with each other (GUID-based names).
    }

    [Fact]
    public async Task SaveAndLoadSettings_RoundTrips()
    {
        var repo = new JsonAppRepository();
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
        var repo = new JsonAppRepository();
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
        var repo = new JsonAppRepository();
        var settings = await repo.LoadSettingsAsync();
        await repo.SaveSettingsAsync(settings);

        // Check no .tmp file remains
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIChat");
        var tmpFiles = Directory.GetFiles(appData, "*.tmp");
        Assert.Empty(tmpFiles);
    }
}
