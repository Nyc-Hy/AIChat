namespace AIChat.Application.Workspace;

public static class WorkspaceCommitBatchRunner
{
    public static Task<WorkspaceCommitResult> CommitAsync(
        IWorkspaceChangeService service,
        string projectPath,
        string message,
        IReadOnlyList<WorkspaceChange> changes,
        CancellationToken cancellationToken = default)
    {
        var paths = changes
            .Select(c => c.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return service.CommitAsync(projectPath, message, paths, cancellationToken);
    }

    public static Task<WorkspaceCommitResult> CommitAsync(
        IWorkspaceChangeService service,
        string projectPath,
        string message,
        string path,
        CancellationToken cancellationToken = default)
    {
        return service.CommitAsync(projectPath, message, [path], cancellationToken);
    }
}
