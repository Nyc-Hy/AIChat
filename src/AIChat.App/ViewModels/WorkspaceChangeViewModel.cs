using AIChat.Application.Workspace;

namespace AIChat.App.ViewModels;

public sealed class WorkspaceChangeViewModel : ObservableObject
{
    private readonly WorkspaceChange _change;

    public WorkspaceChangeViewModel(WorkspaceChange change)
    {
        _change = change;
    }

    public string Status => _change.Status;
    public string Path => _change.Path;
    public string DisplayStatus => _change.DisplayStatus;
    public bool IsUntracked => _change.IsUntracked;
    public bool IsStaged => _change.IsStaged;
}
