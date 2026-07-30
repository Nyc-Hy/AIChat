using System.Collections.ObjectModel;
using AIChat.Application.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Git status / diff viewer. Wraps IWorkspaceChangeService so the user
// can see what the agent (or they) changed in the current project
// without dropping to a terminal. Two surfaces in one VM:
//   - the change list (always refreshed when the modal opens)
//   - the diff for the currently selected file (loaded on demand)
//
// Refresh fetches both the change set and the diff for the previously
// selected file in one round-trip; switching the selection re-uses
// the same code path so the diff updates every time.
//
// Mirrors MemoryEditorViewModel's pattern: holds a back-reference
// to the sidebar so the change list updates when the active project
// changes (e.g. user clicks another project in the sidebar while the
// modal is closed and re-opens it).
public sealed partial class GitStatusViewModel : ViewModelBase
{
    private readonly IWorkspaceChangeService _workspace;
    private readonly ProjectSidebarViewModel _sidebar;

    [ObservableProperty]
    private string branch = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private DateTimeOffset? lastUpdated;

    [ObservableProperty]
    private string? diffText;

    [ObservableProperty]
    private bool isDiffTruncated;

    [ObservableProperty]
    private string? selectedPath;

    public ObservableCollection<GitFileChangeViewModel> Changes { get; } = [];

    public bool IsAvailable => _sidebar.CurrentProject is not null;
    public string ProjectName => _sidebar.CurrentProject?.Name ?? "";
    public int ChangeCount => Changes.Count;
    public bool HasChanges => Changes.Count > 0;
    public bool HasDiff => !string.IsNullOrWhiteSpace(DiffText);
    public string EmptyStateMessage => IsAvailable
        ? "(工作区干净，没有未提交改动)"
        : "(请先选择项目)";

    public GitStatusViewModel(IWorkspaceChangeService workspace, ProjectSidebarViewModel sidebar)
    {
        _workspace = workspace;
        _sidebar = sidebar;

        _sidebar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectSidebarViewModel.CurrentProject)
                                or nameof(ProjectSidebarViewModel.SelectedProjectName))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(EmptyStateMessage));
            }
        };
    }

    // Called by the host on Open. Re-fetches the change set; restores
    // the diff for the previously selected file if it still exists in
    // the new list (so opening the modal on the same file shows the
    // same diff the user was looking at before closing).
    public async Task RefreshAsync()
    {
        var project = _sidebar.CurrentProject;
        if (project is null || string.IsNullOrWhiteSpace(project.Path))
        {
            Branch = "";
            Changes.Clear();
            DiffText = null;
            SelectedPath = null;
            LastUpdated = DateTimeOffset.Now;
            OnPropertyChanged(nameof(ChangeCount));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasDiff));
            return;
        }

        IsLoading = true;
        try
        {
            WorkspaceChangeSet changeSet;
            try
            {
                changeSet = await _workspace.GetChangesAsync(project.Path);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"git 状态读取失败：{ex.Message}";
                return;
            }

            ErrorMessage = null;
            Branch = string.IsNullOrWhiteSpace(changeSet.Branch)
                ? ""
                : changeSet.Branch.TrimStart('#', ' ').Trim();

            var previouslySelected = SelectedPath;
            Changes.Clear();
            foreach (var change in changeSet.Changes)
            {
                Changes.Add(new GitFileChangeViewModel(change, this));
            }
            OnPropertyChanged(nameof(ChangeCount));
            OnPropertyChanged(nameof(HasChanges));

            // Restore selection: prefer the same path if it's still in
            // the list, otherwise default to the first change (most
            // useful default for a fresh open). The diff is loaded
            // synchronously inside the same RefreshAsync so callers
            // (and tests) can rely on DiffText being populated by
            // the time RefreshAsync returns.
            GitFileChangeViewModel? toSelect = null;
            if (!string.IsNullOrEmpty(previouslySelected))
            {
                toSelect = Changes.FirstOrDefault(c =>
                    string.Equals(c.Path, previouslySelected, StringComparison.OrdinalIgnoreCase));
            }
            toSelect ??= Changes.FirstOrDefault();
            SelectedChange = toSelect;
            if (toSelect is not null)
            {
                await LoadDiffAsync(toSelect);
            }

            LastUpdated = DateTimeOffset.Now;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Diff for the currently selected file. The RelayCommand
    // wrapper is what the XAML binds to; the body of the work lives
    // in LoadDiffAsync so RefreshAsync can call it directly (without
    // a fire-and-forget) and tests can assert on the resulting
    // state synchronously.
    [RelayCommand]
    public async Task SelectChangeAsync(GitFileChangeViewModel? change)
    {
        SelectedChange = change;
        if (change is null)
        {
            DiffText = null;
            IsDiffTruncated = false;
            SelectedPath = null;
            OnPropertyChanged(nameof(HasDiff));
            return;
        }
        await LoadDiffAsync(change);
    }

    // Internal worker: fetches the diff for a file, handles errors,
    // updates DiffText / IsDiffTruncated / SelectedPath. IsLoading
    // wraps the call so the refresh button can disable itself while
    // a slow git invocation is in flight.
    private async Task LoadDiffAsync(GitFileChangeViewModel change)
    {
        var project = _sidebar.CurrentProject;
        if (project is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            try
            {
                var diff = await _workspace.GetDiffAsync(project.Path, change.Path);
                DiffText = diff.DiffText;
                IsDiffTruncated = diff.IsTruncated;
                SelectedPath = change.Path;
                OnPropertyChanged(nameof(HasDiff));
            }
            catch (Exception ex)
            {
                DiffText = $"(diff 读取失败：{ex.Message})";
                IsDiffTruncated = false;
                SelectedPath = change.Path;
                OnPropertyChanged(nameof(HasDiff));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [ObservableProperty]
    private GitFileChangeViewModel? selectedChange;

    // When the selection flips, update IsSelected on the affected
    // rows so the XAML can render the persistent selected state
    // (sidebar-row Classes.selected). Without this the only
    // feedback is hover, which disappears the moment the user
    // moves the mouse to the diff panel.
    partial void OnSelectedChangeChanged(GitFileChangeViewModel? value)
    {
        foreach (var change in Changes)
        {
            change.IsSelected = ReferenceEquals(change, value);
        }
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();
}

// Per-row VM. The display status collapses the raw "M " / "??"
// porcelain codes into the human-readable string the user already
// sees in the /git bubble. Background colour is chosen by the XAML
// from StatusKind (no IBrush references here — the view layer owns
// the colour map so theming stays centralised).
public sealed partial class GitFileChangeViewModel : ObservableObject
{
    public string Path { get; }
    public string FileName { get; }
    public string StatusDisplay { get; }
    public string StatusKind { get; }

    // Back-reference so the row's Command can call into the
    // view-model's diff-loading path without bubbling up to the
    // ItemsControl's DataContext.
    private readonly GitStatusViewModel _owner;

    [ObservableProperty]
    private bool isSelected;

    public GitFileChangeViewModel(WorkspaceChange change, GitStatusViewModel owner)
    {
        _owner = owner;
        Path = change.Path;
        FileName = System.IO.Path.GetFileName(change.Path);
        StatusDisplay = change.DisplayStatus;
        StatusKind = ClassifyStatus(change);
    }

    [RelayCommand]
    private Task SelectAsync() => _owner.SelectChangeAsync(this);

    private static string ClassifyStatus(WorkspaceChange change)
    {
        if (change.IsUntracked) return "untracked";
        var first = change.Status.Length > 0 ? change.Status[0] : ' ';
        return first switch
        {
            'M' => "modified",
            'A' => "added",
            'D' => "deleted",
            'R' => "renamed",
            'C' => "copied",
            'U' => "conflict",
            _ => "other"
        };
    }
}
