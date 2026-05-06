using AIChat.Abstractions.Persistence;
using AIChat.Domain.Audit;

namespace AIChat.Application.Audit;

public static class AgentRunAuditLoader
{
    public static async Task<IReadOnlyList<AuditEvent>> LoadAsync(
        IAuditLogRepository repository,
        string projectId,
        string runId,
        DateTimeOffset startedAt,
        int maxCount = 200,
        CancellationToken cancellationToken = default)
    {
        var events = await repository.QueryAsync(
            projectId,
            after: startedAt.AddMinutes(-1),
            runId: runId,
            maxCount: maxCount,
            cancellationToken: cancellationToken);

        return events.OrderBy(e => e.Timestamp).ToList();
    }
}
