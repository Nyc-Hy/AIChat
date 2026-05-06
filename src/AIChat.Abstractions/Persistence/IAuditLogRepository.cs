using AIChat.Domain.Audit;

namespace AIChat.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        string projectId,
        DateTimeOffset? after = null,
        AuditEventType? type = null,
        string? runId = null,
        int maxCount = 200,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        string projectId,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default);

    Task CleanupAsync(
        string projectId,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
