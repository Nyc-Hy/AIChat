using AIChat.Domain.Audit;
using AIChat.Storage.Json;

namespace AIChat.App.ViewModels;

public static class AgentRunAuditLoader
{
    public static async Task<List<AuditEventViewModel>> LoadAsync(
        AuditLogRepository repository,
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

        return events
            .OrderBy(e => e.Timestamp)
            .Select(e => new AuditEventViewModel(e))
            .ToList();
    }
}
