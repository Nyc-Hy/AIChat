using AIChat.Application.Persistence;

namespace AIChat.Tests.Persistence;

// Wave 9 (parity plan §7 Wave 9): pin the small shared
// JSON load / save helper that Scheduled + Sites registries
// use. Atomic write behavior is the most important
// contract: a process kill mid-save must not leave a half-
// written file behind.
public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _file;

    public JsonFileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aichat-json-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _file = Path.Combine(_root, "data.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadListAsync_MissingFile_ReturnsEmpty()
    {
        var items = await JsonFileStore.LoadListAsync<TestRow>(_file);
        Assert.Empty(items);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsList()
    {
        var seed = new List<TestRow>
        {
            new() { Id = "1", Name = "alpha" },
            new() { Id = "2", Name = "beta" },
        };
        await JsonFileStore.SaveListAsync(_file, seed);

        var loaded = await JsonFileStore.LoadListAsync<TestRow>(_file);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("alpha", loaded[0].Name);
    }

    [Fact]
    public async Task LoadListAsync_CorruptedFile_FallsBackToEmpty()
    {
        // A corrupted sidecar must not break the app — the
        // registries fall back to an empty list and the user
        // can re-create the data. The bad file is left on
        // disk for post-mortem inspection.
        await File.WriteAllTextAsync(_file, "{ this is not valid json");

        var items = await JsonFileStore.LoadListAsync<TestRow>(_file);
        Assert.Empty(items);
    }

    [Fact]
    public async Task SaveListAsync_OverwritesExistingFile()
    {
        await JsonFileStore.SaveListAsync(_file, new List<TestRow>
        {
            new() { Id = "1", Name = "first" },
        });
        await JsonFileStore.SaveListAsync(_file, new List<TestRow>
        {
            new() { Id = "2", Name = "second" },
            new() { Id = "3", Name = "third" },
        });

        var loaded = await JsonFileStore.LoadListAsync<TestRow>(_file);
        Assert.Equal(2, loaded.Count);
        Assert.DoesNotContain(loaded, item => item.Id == "1");
    }

    [Fact]
    public async Task SaveListAsync_CreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(_root, "a", "b", "c", "data.json");
        await JsonFileStore.SaveListAsync(nested, new List<TestRow>
        {
            new() { Id = "1", Name = "deep" },
        });

        Assert.True(File.Exists(nested));
        var loaded = await JsonFileStore.LoadListAsync<TestRow>(nested);
        Assert.Single(loaded);
    }

    public sealed class TestRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
