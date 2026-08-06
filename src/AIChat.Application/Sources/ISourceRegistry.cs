using AIChat.Domain.Sources;

namespace AIChat.Application.Sources;

// Wave 7 first slice: the registry the Environment
// panel reads from. Same shape as the other 1.0.1
// registries (Plugin / Scheduled / Site) — JSON-backed
// list, Changed event, CRUD methods.
//
// Scope of the first slice: capture + list + remove
// clipboard text snapshots. Web fetch / connector /
// plugin sources are follow-up slices; the registry's
// surface is generic enough that they plug in without
// a shape change.
public interface ISourceRegistry
{
    IReadOnlyList<Source> Sources { get; }
    event EventHandler? Changed;

    Task ReloadAsync(CancellationToken cancellationToken = default);
    Task<string> AddAsync(Source source, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string sourceId, CancellationToken cancellationToken = default);
}
