using AIChat.Application.Workspace;

namespace AIChat.App.ViewModels;

public static class WorkspaceChangeListBuilder
{
    public static WorkspaceChangeListResult Build(WorkspaceChangeSet changeSet)
    {
        var all = new List<WorkspaceChangeViewModel>();
        var staged = new List<WorkspaceChangeViewModel>();
        var unstaged = new List<WorkspaceChangeViewModel>();
        var untracked = new List<WorkspaceChangeViewModel>();

        foreach (var change in changeSet.Changes)
        {
            var vm = new WorkspaceChangeViewModel(change);
            all.Add(vm);

            if (vm.IsUntracked)
                untracked.Add(vm);
            else if (vm.IsStaged)
                staged.Add(vm);
            else
                unstaged.Add(vm);
        }

        var statusText = changeSet.HasChanges
            ? $"{changeSet.Changes.Count} 个变更{(changeSet.IsTruncated ? "，列表已截断" : "")}"
            : "工作区干净";

        return new WorkspaceChangeListResult
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

public sealed class WorkspaceChangeListResult
{
    public string Branch { get; init; } = "";
    public string StatusText { get; init; } = "";
    public IReadOnlyList<WorkspaceChangeViewModel> All { get; init; } = [];
    public IReadOnlyList<WorkspaceChangeViewModel> Staged { get; init; } = [];
    public IReadOnlyList<WorkspaceChangeViewModel> Unstaged { get; init; } = [];
    public IReadOnlyList<WorkspaceChangeViewModel> Untracked { get; init; } = [];
}
