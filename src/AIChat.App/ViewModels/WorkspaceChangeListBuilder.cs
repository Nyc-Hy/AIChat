using AIChat.Application.Workspace;

namespace AIChat.App.ViewModels;

public static class WorkspaceChangeListBuilder
{
    public static WorkspaceChangeListResult Build(WorkspaceChangeSet changeSet)
    {
        var grouped = WorkspaceChangeGrouper.Group(changeSet);

        return new WorkspaceChangeListResult
        {
            Branch = grouped.Branch,
            StatusText = grouped.StatusText,
            All = grouped.All.Select(c => new WorkspaceChangeViewModel(c)).ToList(),
            Staged = grouped.Staged.Select(c => new WorkspaceChangeViewModel(c)).ToList(),
            Unstaged = grouped.Unstaged.Select(c => new WorkspaceChangeViewModel(c)).ToList(),
            Untracked = grouped.Untracked.Select(c => new WorkspaceChangeViewModel(c)).ToList()
        };
    }
}

public sealed class WorkspaceChangeListResult
{
    public string Branch { get; init; } = "";
    public string StatusText { get; init; } = "";
    public IReadOnlyList<WorkspaceChangeViewModel> All { get; init; } = [];
    public IReadOnlyList<WorkspaceChangeViewModel> Staged { get; init; } = [];
    public IReadOnlyList<WorkspaceChangeViewModel> Unstaged { get; init; } = [];
    public IReadOnlyList<WorkspaceChangeViewModel> Untracked { get; init; } = [];
}
