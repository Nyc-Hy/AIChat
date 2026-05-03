using AIChat.Domain.Audit;
using AIChat.Storage.Json;

namespace AIChat.Application.Audit;

public static class AuditEventRecorder
{
    public static async Task RecordAsync(
        AuditLogRepository? repository,
        AuditEventType type,
        string projectId,
        string runId,
        string toolName = "",
        string summary = "",
        string detail = "",
        CancellationToken cancellationToken = default)
    {
        if (repository is null) return;
        try
        {
            await repository.AppendAsync(new AuditEvent
            {
                ProjectId = projectId,
                RunId = runId,
                Type = type,
                ToolName = toolName,
                Summary = summary,
                Detail = detail
            }, cancellationToken);
        }
        catch
        {
            // Audit logging is best-effort; don't break the caller.
        }
    }
}
