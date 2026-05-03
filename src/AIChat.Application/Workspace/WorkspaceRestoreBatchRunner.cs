namespace AIChat.Application.Workspace;

public sealed class WorkspaceRestoreBatchResult
{
    public int Restored { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public static class WorkspaceRestoreBatchRunner
{
    public static async Task<WorkspaceRestoreBatchResult> RestoreAsync(
        IWorkspaceChangeService service,
        string projectPath,
        IReadOnlyList<WorkspaceChange> changes,
        CancellationToken cancellationToken = default)
    {
        var restored = 0;
        var errors = new List<string>();

        foreach (var change in changes)
        {
            try
            {
                _ = await service.RestoreFileAsync(
                    projectPath,
                    change.Path,
                    deleteUntracked: change.IsUntracked,
                    cancellationToken);
                restored++;
            }
            catch (Exception ex)
            {
                errors.Add($"{change.Path}: {ex.Message}");
            }
        }

        return new WorkspaceRestoreBatchResult
        {
            Restored = restored,
            Errors = errors
        };
    }
}
