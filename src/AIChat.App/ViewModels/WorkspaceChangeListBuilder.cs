using AIChat.Application.Workspace;

namespace AIChat.App.ViewModels;

public static class WorkspaceChangeListBuilder
{
    public static WorkspaceChangeListResult Build(WorkspaceChangeSet changeSet)
    {
        var grouped = WorkspaceChangeGrouper.Group(changeSet);

        // Create VMs once; reuse the same instances across all lists so that
        // PropertyChanged handlers and selection state are shared.
        var all = grouped.All.Select(c => new WorkspaceChangeViewModel(c)).ToList();
        var lookup = new Dictionary<WorkspaceChange, WorkspaceChangeViewModel>(grouped.All.Count);
        for (var i = 0; i < grouped.All.Count; i++)
        {
            lookup[grouped.All[i]] = all[i];
        }

        return new WorkspaceChangeListResult
        {
            Branch = grouped.Branch,
            StatusText = grouped.StatusText,
            All = all,
            Staged = grouped.Staged.Select(c => lookup[c]).ToList(),
            Unstaged = grouped.Unstaged.Select(c => lookup[c]).ToList(),
            Untracked = grouped.Untracked.Select(c => lookup[c]).ToList()
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
