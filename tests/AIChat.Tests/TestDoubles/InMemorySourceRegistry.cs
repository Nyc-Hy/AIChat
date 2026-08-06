using AIChat.Application.Sources;
using AIChat.Domain.Sources;

namespace AIChat.Tests.TestDoubles;

// In-memory ISourceRegistry for unit tests. Stores
// sources in a List<Source> (no JSON, no Changed event
// plumbing). Mirrors the same shape as
// InMemoryAppRepository so the existing tests can wire
// it without touching the disk.
public sealed class InMemorySourceRegistry : ISourceRegistry
{
    private readonly List<Source> _sources = [];

    public IReadOnlyList<Source> Sources => _sources.AsReadOnly();

    public event EventHandler? Changed;

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string> AddAsync(Source source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            source.Id = Guid.NewGuid().ToString("N");
        }
        if (source.CapturedAt == default)
        {
            source.CapturedAt = DateTimeOffset.UtcNow;
        }
        _sources.Add(source);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(source.Id);
    }

    public Task<bool> RemoveAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var i = _sources.FindIndex(s => s.Id == sourceId);
        if (i < 0)
        {
            return Task.FromResult(false);
        }
        _sources.RemoveAt(i);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }
}
