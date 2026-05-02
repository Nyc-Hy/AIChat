using System.Text.Json;
using AIChat.Domain.Audit;

namespace AIChat.Storage.Json;

public sealed class AuditLogRepository
{
    private readonly string _basePath;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxArchiveCount;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public AuditLogRepository(string basePath, long maxFileSizeBytes = 5 * 1024 * 1024, int maxArchiveCount = 3)
    {
        _basePath = basePath;
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxArchiveCount = maxArchiveCount;
    }

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var filePath = GetLogPath(auditEvent.ProjectId);
        var directory = Path.GetDirectoryName(filePath);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(auditEvent, JsonOptions) + "\n";
        await File.AppendAllTextAsync(filePath, line, cancellationToken);

        // Rotate after writing if file now exceeds limit
        if (new FileInfo(filePath).Length >= _maxFileSizeBytes)
        {
            RotateFile(filePath);
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        string projectId,
        DateTimeOffset? after = null,
        AuditEventType? type = null,
        string? runId = null,
        int maxCount = 200,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AuditEvent>();

        // Read from current file and archives (newest first)
        var files = GetLogFilesNewestFirst(projectId);
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            var lines = await File.ReadAllLinesAsync(file, cancellationToken);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions);
                if (entry is null) continue;
                if (after.HasValue && entry.Timestamp < after.Value) continue;
                if (type.HasValue && entry.Type != type.Value) continue;
                if (!string.IsNullOrWhiteSpace(runId) &&
                    !string.Equals(entry.RunId, runId, StringComparison.Ordinal)) continue;
                events.Add(entry);
            }
        }

        // Sort by timestamp descending and take last N
        events.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return events.Take(maxCount).ToList();
    }

    public async Task<int> CountAsync(
        string projectId,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetLogPath(projectId);
        if (!File.Exists(filePath))
        {
            return 0;
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var count = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions);
            if (entry is null) continue;
            if (after.HasValue && entry.Timestamp < after.Value) continue;
            count++;
        }

        return count;
    }

    public Task CleanupAsync(string projectId, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var files = GetLogFilesNewestFirst(projectId);
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            // Always keep the current (un-suffixed) file
            if (!file.Contains(".jsonl.")) continue;

            var lastWrite = File.GetLastWriteTimeUtc(file);
            if (lastWrite < olderThan.UtcDateTime)
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    private void RotateFile(string filePath)
    {
        // Shift existing archives: .N → delete, .(N-1) → .N, …, .1 → .2, current → .1
        for (var i = _maxArchiveCount; i >= 1; i--)
        {
            var src = i == 1 ? filePath : $"{filePath}.{i - 1}";
            var dst = $"{filePath}.{i}";

            if (i == _maxArchiveCount)
            {
                // Delete the oldest archive to make room
                if (File.Exists(dst)) File.Delete(dst);
            }

            if (File.Exists(src))
            {
                File.Move(src, dst, overwrite: true);
            }
        }

        // The current file was just moved to .1. Create a fresh empty file so
        // the next AppendAsync starts from a clean slate.
        File.Create(filePath).Dispose();
    }

    private string[] GetLogFilesNewestFirst(string projectId)
    {
        var basePath = GetLogPath(projectId);
        var files = new List<string> { basePath };
        for (var i = 1; i <= _maxArchiveCount; i++)
        {
            files.Add($"{basePath}.{i}");
        }

        // Return current file first (newest data), then archives in order
        return files.ToArray();
    }

    private string GetLogPath(string projectId)
    {
        var safeId = string.IsNullOrWhiteSpace(projectId) ? "_global" : projectId;
        return Path.Combine(_basePath, "audit", $"{safeId}.jsonl");
    }
}
