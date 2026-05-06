using AIChat.Abstractions.Persistence;
using AIChat.Domain.Audit;

namespace AIChat.Application.Audit;

public sealed class AgentRunAuditService
{
    private readonly IAuditLogRepository? _repository;

    public AgentRunAuditService(IAuditLogRepository? repository)
    {
        _repository = repository;
    }

    public bool IsAvailable => _repository is not null;

    public Task<IReadOnlyList<AuditEvent>> LoadRunEventsAsync(
        string projectId,
        string runId,
        DateTimeOffset startedAt,
        int maxCount = 200,
        CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            return Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        }

        return AgentRunAuditLoader.LoadAsync(
            _repository,
            projectId,
            runId,
            startedAt,
            maxCount,
            cancellationToken);
    }

    public Task RecordAsync(
        AuditEventType type,
        string projectId,
        string runId,
        string toolName = "",
        string summary = "",
        string detail = "",
        CancellationToken cancellationToken = default)
    {
        return AuditEventRecorder.RecordAsync(
            _repository,
            type,
            projectId,
            runId,
            toolName,
            summary,
            detail,
            cancellationToken);
    }
}
