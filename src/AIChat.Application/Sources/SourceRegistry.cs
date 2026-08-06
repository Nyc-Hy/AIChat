using AIChat.Abstractions.Configuration;
using AIChat.Application.Persistence;
using AIChat.Domain.Sources;

namespace AIChat.Application.Sources;

// JSON-backed source registry. Same pattern as
// ScheduledTaskRegistry / SiteRegistry: atomic write
// via JsonFileStore, thread-safe mutation under `_gate`,
// Changed event on the captured instance.
public sealed class SourceRegistry : ISourceRegistry
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private List<Source> _sources = [];

    public SourceRegistry(string? filePath = null)
    {
        _filePath = filePath ?? AppRuntimeProfile.SourcesFile;
    }

    public IReadOnlyList<Source> Sources
    {
        get { lock (_gate) { return _sources.ToArray(); } }
    }

    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await JsonFileStore
            .LoadListAsync<Source>(_filePath, cancellationToken)
            .ConfigureAwait(false);
        lock (_gate)
        {
            _sources = loaded;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> AddAsync(Source source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            source.Id = Guid.NewGuid().ToString("N");
        }
        if (source.CapturedAt == default)
        {
            source.CapturedAt = DateTimeOffset.UtcNow;
        }

        List<Source> snapshot;
        lock (_gate)
        {
            _sources.Add(source);
            snapshot = _sources.ToList();
        }
        await JsonFileStore.SaveListAsync(_filePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return source.Id;
    }

    public async Task<bool> RemoveAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return false;
        }
        List<Source> snapshot;
        lock (_gate)
        {
            var index = _sources.FindIndex(s => s.Id == sourceId);
            if (index < 0)
            {
                return false;
            }
            _sources.RemoveAt(index);
            snapshot = _sources.ToList();
        }
        await JsonFileStore.SaveListAsync(_filePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
