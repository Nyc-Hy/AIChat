using System.Collections.ObjectModel;
using AIChat.Application.Workspace;
using AIChat.Domain.Projects;
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

    // Human-readable "12:34:56" timestamp for the header so the user
    // can tell whether the change list + diff are fresh or stale
    // without re-running git. Absolute time on purpose — a relative
    // ("5 分钟前") form would need a timer to stay accurate while
    // the modal is open, and the user typically re-runs refresh
    // before checking anyway.
    public string LastUpdatedDisplay => LastUpdated?.ToLocalTime().ToString("HH:mm:ss") ?? "";
    public bool HasLastUpdated => LastUpdated.HasValue;

    [ObservableProperty]
    private string? diffText;

    [ObservableProperty]
    private bool isDiffTruncated;

    [ObservableProperty]
    private string? selectedPath;

    // Stage / commit / restore surface (XAML-bound). 真功能(走
    // IWorkspaceChangeService.StageAsync / CommitAsync / RestoreFileAsync)
    // 由 Wave 5 补。当前 Wave 2 只加让 build 过的最小 stub。
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string commitMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StageSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnstageSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestRestoreSelectedCommand))]
    private bool isMutating;

    [ObservableProperty]
    private string operationMessage = "";

    [ObservableProperty]
    private bool isRestoreConfirmationOpen;

    public string RestoreConfirmationText => SelectedChange?.IsUntracked == true
        ? $"“{SelectedChange.Path}” 是未跟踪文件，继续会永久删除该文件。"
        : $"撤销 “{SelectedChange?.Path}” 的未提交改动？此操作无法从 AIChat 内恢复。";

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
        if (project is null || string.IsNullOrWhiteSpace(project.TryGetPrimaryPath()))
        {
            Branch = "";
            Changes.Clear();
            DiffText = null;
            SelectedPath = null;
            // Don't touch LastUpdated here — the "no project" state
            // is not a refresh outcome, the header should read as
            // empty / pre-first-refresh rather than "更新于 刚刚"
            // for a modal that has no data behind the timestamp.
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
                changeSet = await _workspace.GetChangesAsync(project.TryGetPrimaryPath());
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
                var diff = await _workspace.GetDiffAsync(project.TryGetPrimaryPath(), change.Path);
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

    partial void OnLastUpdatedChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastUpdatedDisplay));
        OnPropertyChanged(nameof(HasLastUpdated));
    }

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

    // ===== Wave 6: real stage / unstage / restore wiring =====
    // All four commands route through IWorkspaceChangeService. The
    // path argument is the file path (relative to the project root
    // for the porcelain shell-out). After every successful mutation
    // we call RefreshAsync so the diff list / counts stay in sync
    // with on-disk state.

    [RelayCommand]
    private async Task StageSelected()
    {
        if (SelectedChange is null) return;
        var project = _sidebar.CurrentProject;
        var path = project?.TryGetPrimaryPath();
        if (project is null || string.IsNullOrEmpty(path)) return;
        try
        {
            await _workspace.StageAsync(path, new[] { SelectedChange.Path });
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StageError = $"Stage 失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UnstageSelected()
    {
        if (SelectedChange is null) return;
        var project = _sidebar.CurrentProject;
        var path = project?.TryGetPrimaryPath();
        if (project is null || string.IsNullOrEmpty(path)) return;
        try
        {
            await _workspace.UnstageAsync(path, new[] { SelectedChange.Path });
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StageError = $"Unstage 失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void RequestRestoreSelected() => IsRestoreConfirmationOpen = true;

    [RelayCommand]
    private void CancelRestoreSelected() => IsRestoreConfirmationOpen = false;

    [RelayCommand]
    private async Task ConfirmRestoreSelected()
    {
        IsRestoreConfirmationOpen = false;
        if (SelectedChange is null) return;
        var project = _sidebar.CurrentProject;
        var path = project?.TryGetPrimaryPath();
        if (project is null || string.IsNullOrEmpty(path)) return;
        try
        {
            var deleteUntracked = SelectedChange.IsUntracked == true;
            await _workspace.RestoreFileAsync(
                path,
                SelectedChange.Path,
                deleteUntracked: deleteUntracked);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StageError = $"Restore 失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Commit()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage)) return;
        var project = _sidebar.CurrentProject;
        var path = project?.TryGetPrimaryPath();
        if (project is null || string.IsNullOrEmpty(path)) return;
        try
        {
            // Staged paths are the union of all selected changes plus
            // the file the user is currently looking at. The service
            // accepts a list so the user can scope a commit to a
            // subset of the staged changes. We pass the full list of
            // paths the user touched (selected + already-staged) so
            // the service can decide which paths to actually pass to
            // `git commit -- <paths>`.
            var paths = Changes
                .Where(c => c.IsSelected)
                .Select(c => c.Path)
                .ToList();
            var result = await _workspace.CommitAsync(path, CommitMessage.Trim(), paths);
            CommitMessage = "";
            // CommitAsync throws on failure; the result object is
            // the success payload (commit hash + message + paths).
            LastCommitDisplay = result.Message ?? CommitMessage;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StageError = $"Commit 失败：{ex.Message}";
        }
    }

    [ObservableProperty]
    private string stageError = "";

    [ObservableProperty]
    private string lastCommitDisplay = "";
}

// Per-row VM. The display status collapses the raw "M " / "??"
// porcelain codes into the human-readable string the user already
// sees in the /git bubble. The per-status class on the badge
// (modified / added / deleted / untracked / renamed / copied /
// conflict) is chosen by the XAML from the matching IsX bool, so
// the colour map stays in the view layer.
public sealed partial class GitFileChangeViewModel : ObservableObject
{
    public string Path { get; }
    public string FileName { get; }
    public string StatusDisplay { get; }

    // Per-status flags the XAML binds as class selectors so the
    // git-status-badge style can colour the background + foreground
    // by outcome. One flag per recognised porcelain status (no
    // "other" — that case keeps the neutral default style). The
    // flags are computed once in the constructor from the porcelain
    // code, so they never flip after construction and don't need
    // INotifyPropertyChanged.
    public bool IsModified { get; }
    public bool IsAdded { get; }
    public bool IsDeleted { get; }
    public bool IsUntracked { get; }
    public bool IsRenamed { get; }
    public bool IsCopied { get; }
    public bool IsConflict { get; }

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
        var kind = ClassifyStatus(change);
        IsModified = kind == "modified";
        IsAdded = kind == "added";
        IsDeleted = kind == "deleted";
        IsUntracked = kind == "untracked";
        IsRenamed = kind == "renamed";
        IsCopied = kind == "copied";
        IsConflict = kind == "conflict";
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
