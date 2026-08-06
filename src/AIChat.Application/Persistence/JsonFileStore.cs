using System.Text.Json;

namespace AIChat.Application.Persistence;

// Shared load / save helper for the small JSON sidecar
// files the Wave 9 registries (Scheduled + Sites) own.
// Each file is tiny (handful of rows), so we rewrite the
// whole list on every mutation instead of doing per-row
// diffs — the cost is negligible and the consistency
// story is "the file on disk matches the in-memory list
// at the moment of the last save".
//
// Threading: LoadAsync and SaveAsync are safe to call
// concurrently with each other, but the caller is
// responsible for serialising mutations that flow through
// the same registry instance. The Scheduled / Sites
// registries take a lock around their read-modify-write
// sections; the file store just protects the I/O.
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<List<T>> LoadListAsync<T>(string filePath, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var items = await JsonSerializer.DeserializeAsync<List<T>>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
            return items ?? [];
        }
        catch
        {
            // A corrupted sidecar must not break the app —
            // fall back to an empty list and let the user
            // re-create their data. The bad file is left
            // on disk for post-mortem inspection.
            return [];
        }
    }

    public static async Task SaveListAsync<T>(string filePath, IReadOnlyList<T> items, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Atomic write: serialise to a per-call unique
        // temp file in the same directory, then move it
        // over the target. Per-call uniqueness avoids
        // the race where two concurrent writers pick
        // the same .tmp path — the first finishes its
        // move, the second then overwrites its own .tmp
        // and re-tries, but the File.Create + File.Move
        // pair doesn't serialise the gap.
        var tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, items, Options, cancellationToken)
                .ConfigureAwait(false);
        }
        // File.Move with overwrite is atomic on macOS
        // / Linux (rename(2)). On Windows it falls back
        // to MoveFileExW with MOVEFILE_REPLACE_EXISTING;
        // still atomic for files on the same volume.
        File.Move(tempPath, filePath, overwrite: true);
    }
}
