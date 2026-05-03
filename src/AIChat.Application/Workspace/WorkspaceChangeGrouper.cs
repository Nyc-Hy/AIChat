namespace AIChat.Application.Workspace;

public static class WorkspaceChangeGrouper
{
    public static WorkspaceChangeGroupResult Group(WorkspaceChangeSet changeSet)
    {
        var all = new List<WorkspaceChange>();
        var staged = new List<WorkspaceChange>();
        var unstaged = new List<WorkspaceChange>();
        var untracked = new List<WorkspaceChange>();

        foreach (var change in changeSet.Changes)
        {
            all.Add(change);

            if (change.IsUntracked)
                untracked.Add(change);
            else if (change.IsStaged)
                staged.Add(change);
            else
                unstaged.Add(change);
        }

        var statusText = changeSet.HasChanges
            ? $"{changeSet.Changes.Count} 个变更{(changeSet.IsTruncated ? "，列表已截断" : "")}"
            : "工作区干净";

        return new WorkspaceChangeGroupResult
        {
            Branch = changeSet.Branch,
            StatusText = statusText,
            All = all,
            Staged = staged,
            Unstaged = unstaged,
            Untracked = untracked
        };
    }
}

public sealed class WorkspaceChangeGroupResult
{
    public string Branch { get; init; } = "";
    public string StatusText { get; init; } = "";
    public IReadOnlyList<WorkspaceChange> All { get; init; } = [];
    public IReadOnlyList<WorkspaceChange> Staged { get; init; } = [];
    public IReadOnlyList<WorkspaceChange> Unstaged { get; init; } = [];
    public IReadOnlyList<WorkspaceChange> Untracked { get; init; } = [];
}
