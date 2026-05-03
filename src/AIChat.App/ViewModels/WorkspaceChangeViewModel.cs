using AIChat.Application.Workspace;

namespace AIChat.App.ViewModels;

public sealed class WorkspaceChangeViewModel : ObservableObject
{
    private readonly WorkspaceChange _change;
    private bool _isSelected;

    public WorkspaceChangeViewModel(WorkspaceChange change)
    {
        _change = change;
    }

    public string Status => _change.Status;
    public string Path => _change.Path;
    public string DisplayStatus => _change.DisplayStatus;
    public bool IsUntracked => _change.IsUntracked;
    public bool IsStaged => _change.IsStaged;
    public bool IsUnstaged => _change.IsUnstaged;
    public bool HasUnstagedChanges => _change.HasUnstagedChanges;
    public string Section => IsUntracked ? "未跟踪" : IsStaged ? "已暂存" : "未暂存";
    public WorkspaceChange Change => _change;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
