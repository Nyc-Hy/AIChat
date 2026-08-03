using AIChat.Application.Sources;
using AIChat.Domain.Sources;

namespace AIChat.Tests.Sources;

// The Source registry is the same shape as the other
// 1.0.1 registries (Scheduled / Site / Plugin) — load
// from a JSON file, atomic save, Changed event, basic
// CRUD. The tests use a per-test scratch file so the
// real ~/.AIChat/ isn't polluted.
public class SourceRegistryTests : IDisposable
{
    private readonly string _scratchDir;

    public SourceRegistryTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "aichat-sources-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratchDir))
            {
                Directory.Delete(_scratchDir, recursive: true);
            }
        }
        catch { /* best effort */ }
    }

    private SourceRegistry NewRegistry() =>
        new(Path.Combine(_scratchDir, "sources.json"));

    [Fact]
    public async Task AddAsync_ThenList_ReturnsPersistedSource()
    {
        var registry = NewRegistry();
        var source = new Source
        {
            Kind = "clipboard",
            DisplayName = "Meeting notes",
            Content = "Line 1\nLine 2",
        };

        await registry.AddAsync(source);

        Assert.Single(registry.Sources);
        var stored = registry.Sources[0];
        Assert.Equal(source.Id, stored.Id);
        Assert.Equal("clipboard", stored.Kind);
        Assert.Equal("Meeting notes", stored.DisplayName);
        Assert.Equal("Line 1\nLine 2", stored.Content);
        Assert.NotEqual(default, stored.CapturedAt);
    }

    [Fact]
    public async Task AddAsync_GeneratesIdWhenBlank()
    {
        var registry = NewRegistry();
        await registry.AddAsync(new Source
        {
            Kind = "clipboard",
            DisplayName = "no-id",
            Content = "x",
        });
        Assert.False(string.IsNullOrEmpty(registry.Sources[0].Id));
    }

    [Fact]
    public async Task AddAsync_PersistsAcrossNewInstance()
    {
        // Write through one registry instance, read
        // back through another — the JSON file is the
        // single source of truth, the in-memory list
        // is just a cache.
        var path = Path.Combine(_scratchDir, "sources.json");
        var writer = new SourceRegistry(path);
        await writer.AddAsync(new Source
        {
            Id = "explicit",
            Kind = "clipboard",
            DisplayName = "persistent",
            Content = "survives restart",
        });

        var reader = new SourceRegistry(path);
        await reader.ReloadAsync();

        Assert.Single(reader.Sources);
        Assert.Equal("explicit", reader.Sources[0].Id);
        Assert.Equal("survives restart", reader.Sources[0].Content);
    }

    [Fact]
    public async Task RemoveAsync_KnownId_RemovesAndPersists()
    {
        var registry = NewRegistry();
        var source = new Source { Kind = "clipboard", DisplayName = "x", Content = "y" };
        await registry.AddAsync(source);

        var removed = await registry.RemoveAsync(source.Id);

        Assert.True(removed);
        Assert.Empty(registry.Sources);
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_ReturnsFalse()
    {
        var registry = NewRegistry();
        Assert.False(await registry.RemoveAsync("does-not-exist"));
    }

    [Fact]
    public async Task ReloadAsync_OnEmptyFile_StartsEmpty()
    {
        var registry = NewRegistry();
        await registry.ReloadAsync();
        Assert.Empty(registry.Sources);
    }

    [Fact]
    public async Task Changed_FiresOnAdd()
    {
        var registry = NewRegistry();
        var fired = 0;
        registry.Changed += (_, _) => fired++;

        await registry.AddAsync(new Source
        {
            Kind = "clipboard",
            DisplayName = "x",
            Content = "y",
        });

        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Changed_FiresOnRemove()
    {
        var registry = NewRegistry();
        var source = new Source { Kind = "clipboard", DisplayName = "x", Content = "y" };
        await registry.AddAsync(source);
        var fired = 0;
        registry.Changed += (_, _) => fired++;

        await registry.RemoveAsync(source.Id);

        Assert.Equal(1, fired);
    }
}
