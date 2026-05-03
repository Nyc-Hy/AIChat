namespace AIChat.Application.Workspace;

public sealed class WorkspaceStageBatchResult
{
    public int Count { get; init; }
}

public static class WorkspaceStageBatchRunner
{
    public static async Task<WorkspaceStageBatchResult> StageAsync(
        IWorkspaceChangeService service,
        string projectPath,
        IReadOnlyList<WorkspaceChange> changes,
        CancellationToken cancellationToken = default)
    {
        var paths = changes
            .Select(c => c.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await service.StageAsync(projectPath, paths, cancellationToken);
        return new WorkspaceStageBatchResult { Count = paths.Count };
    }

    public static async Task<WorkspaceStageBatchResult> UnstageAsync(
        IWorkspaceChangeService service,
        string projectPath,
        IReadOnlyList<WorkspaceChange> changes,
        CancellationToken cancellationToken = default)
    {
        var paths = changes
            .Where(c => c.IsStaged)
            .Select(c => c.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await service.UnstageAsync(projectPath, paths, cancellationToken);
        return new WorkspaceStageBatchResult { Count = paths.Count };
    }
}
