using System.Text.Json;
using AIChat.Domain.Audit;

namespace AIChat.Storage.Json;

public sealed class AuditLogRepository
{
    private readonly string _basePath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public AuditLogRepository(string basePath)
    {
        _basePath = basePath;
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
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        string projectId,
        DateTimeOffset? after = null,
        AuditEventType? type = null,
        int maxCount = 200,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetLogPath(projectId);
        if (!File.Exists(filePath))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var events = new List<AuditEvent>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions);
            if (entry is null) continue;
            if (after.HasValue && entry.Timestamp < after.Value) continue;
            if (type.HasValue && entry.Type != type.Value) continue;
            events.Add(entry);
        }

        return events.TakeLast(maxCount).ToList();
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

    private string GetLogPath(string projectId)
    {
        var safeId = string.IsNullOrWhiteSpace(projectId) ? "_global" : projectId;
        return Path.Combine(_basePath, "audit", $"{safeId}.jsonl");
    }
}
