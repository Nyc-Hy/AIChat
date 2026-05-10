using System.Windows;
using AIChat.App.Controls;
using AIChat.Application.Workspace;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool HasWorkspaceChanges => WorkspaceChanges.Count > 0;
    public bool HasSelectedWorkspaceChanges => WorkspaceChanges.Any(change => change.IsSelected);
    public string WorkspaceSelectionText
    {
        get
        {
            var selectedCount = WorkspaceChanges.Count(change => change.IsSelected);
            return selectedCount == 0 ? "未选择文件" : $"已选择 {selectedCount} 个文件";
        }
    }
    public string WorkspaceBranch
    {
        get => _workspaceBranch;
        private set => SetProperty(ref _workspaceBranch, value);
    }
    public string WorkspaceStatusText
    {
        get => _workspaceStatusText;
        private set => SetProperty(ref _workspaceStatusText, value);
    }
    public string WorkspaceDiffText
    {
        get => _workspaceDiffText;
        private set
        {
            if (SetProperty(ref _workspaceDiffText, value))
            {
                WorkspaceDiffLines = DiffLineViewModel.FromDiff(value);
                CopyWorkspaceDiffCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public IReadOnlyList<DiffLineViewModel> WorkspaceDiffLines
    {
        get => _workspaceDiffLines;
        private set => SetProperty(ref _workspaceDiffLines, value);
    }
    public bool IsRefreshingWorkspaceChanges
    {
        get => _isRefreshingWorkspaceChanges;
        private set
        {
            if (SetProperty(ref _isRefreshingWorkspaceChanges, value))
            {
                RaiseWorkspaceCommandStates();
            }
        }
    }
    public WorkspaceChangeViewModel? SelectedWorkspaceChange
    {
        get => _selectedWorkspaceChange;
        set
        {
            if (SetProperty(ref _selectedWorkspaceChange, value))
            {
                RestoreWorkspaceFileCommand.RaiseCanExecuteChanged();
                CommitWorkspaceFileCommand.RaiseCanExecuteChanged();
                OpenWorkspaceFileCommand.RaiseCanExecuteChanged();
                CopyWorkspacePathCommand.RaiseCanExecuteChanged();
                _ = LoadSelectedWorkspaceDiffAsync();
            }
        }
    }

    private async Task RefreshWorkspaceChangesAsync()
    {
        if (SelectedProject is null || IsRefreshingWorkspaceChanges)
        {
            return;
        }

        IsRefreshingWorkspaceChanges = true;
        try
        {
            var changeSet = await _workspaceChangeService.GetChangesAsync(SelectedProject.Path);
            var result = WorkspaceChangeListBuilder.Build(changeSet);

            WorkspaceChanges.Clear();
            StagedWorkspaceChanges.Clear();
            UnstagedWorkspaceChanges.Clear();
            UntrackedWorkspaceChanges.Clear();
            foreach (var viewModel in result.All)
            {
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(WorkspaceChangeViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(HasSelectedWorkspaceChanges));
                        OnPropertyChanged(nameof(WorkspaceSelectionText));
                        RaiseWorkspaceCommandStates();
                    }
                };
                WorkspaceChanges.Add(viewModel);
            }
            foreach (var vm in result.Staged) StagedWorkspaceChanges.Add(vm);
            foreach (var vm in result.Unstaged) UnstagedWorkspaceChanges.Add(vm);
            foreach (var vm in result.Untracked) UntrackedWorkspaceChanges.Add(vm);

            WorkspaceBranch = result.Branch;
            WorkspaceStatusText = result.StatusText;
            SelectedWorkspaceChange = WorkspaceChanges.FirstOrDefault();
            if (SelectedWorkspaceChange is null)
            {
                WorkspaceDiffText = "当前没有可查看的工作区变更。";
            }

            OnPropertyChanged(nameof(HasWorkspaceChanges));
            OnPropertyChanged(nameof(HasSelectedWorkspaceChanges));
            OnPropertyChanged(nameof(WorkspaceSelectionText));
            RaiseWorkspaceCommandStates();
        }
        catch (Exception ex)
        {
            WorkspaceStatusText = $"读取失败：{ex.Message}";
            WorkspaceDiffText = "无法读取当前项目的 Git 状态。";
        }
        finally
        {
            IsRefreshingWorkspaceChanges = false;
        }
    }

    private async Task<WorkspaceRunSnapshot> CaptureWorkspaceSnapshotAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null)
        {
            return new WorkspaceRunSnapshot("", 0, false);
        }

        try
        {
            var changeSet = await _workspaceChangeService.GetChangesAsync(
                SelectedProject.Path,
                maxFiles: 1_000,
                cancellationToken);
            return new WorkspaceRunSnapshot(
                changeSet.Branch,
                changeSet.Changes.Count,
                changeSet.IsTruncated);
        }
        catch
        {
            return new WorkspaceRunSnapshot(WorkspaceBranch, WorkspaceChanges.Count, false);
        }
    }

    private async Task LoadSelectedWorkspaceDiffAsync()
    {
        var version = ++_workspaceDiffLoadVersion;
        if (SelectedProject is null || SelectedWorkspaceChange is null)
        {
            WorkspaceDiffText = "选择一个变更文件查看 diff。";
            return;
        }

        WorkspaceDiffText = "正在读取 diff...";
        try
        {
            var showStagedDiff = WorkspaceDiffFormatter.ShouldShowStagedDiff(
                SelectedWorkspaceChange.IsStaged,
                SelectedWorkspaceChange.HasUnstagedChanges);
            var diff = await _workspaceChangeService.GetDiffAsync(
                SelectedProject.Path,
                SelectedWorkspaceChange.Path,
                staged: showStagedDiff);
            if (version != _workspaceDiffLoadVersion)
            {
                return;
            }

            WorkspaceDiffText = WorkspaceDiffFormatter.FormatDiffText(diff);
        }
        catch (Exception ex)
        {
            if (version == _workspaceDiffLoadVersion)
            {
                WorkspaceDiffText = $"读取 diff 失败：{ex.Message}";
            }
        }
    }

    private async Task RestoreSelectedWorkspaceFileAsync()
    {
        if (SelectedProject is null || SelectedWorkspaceChange is null)
        {
            return;
        }

        var change = SelectedWorkspaceChange;
        var message = WorkspaceOperationTextFormatter.RestoreSingleFileConfirm(change.IsUntracked, change.Path);
        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            message,
            "确认恢复文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await _workspaceChangeService.RestoreFileAsync(
                SelectedProject.Path,
                change.Path,
                deleteUntracked: change.IsUntracked);
            StatusText = WorkspaceOperationTextFormatter.RestoreSingleFileSuccess(result.DeletedUntracked, result.Path);
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = WorkspaceOperationTextFormatter.RestoreError(ex.Message);
            WorkspaceDiffText = WorkspaceOperationTextFormatter.RestoreError(ex.Message);
        }
    }

    private async Task RestoreSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges();
        if (changes.Count == 0)
        {
            await RestoreSelectedWorkspaceFileAsync();
            return;
        }

        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            WorkspaceOperationTextFormatter.RestoreSelectedConfirm(changes.Count),
            "确认恢复已选文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await WorkspaceRestoreBatchRunner.RestoreAsync(
            _workspaceChangeService, SelectedProject.Path,
            changes.Select(c => c.Change).ToList());

        StatusText = WorkspaceOperationTextFormatter.RestoreMultipleSuccess(result.Restored, result.Errors.Count);
        if (result.Errors.Count > 0)
        {
            WorkspaceDiffText = string.Join(Environment.NewLine, result.Errors);
        }

        await RefreshWorkspaceChangesAsync();
    }

    private async Task CommitSelectedWorkspaceFileAsync()
    {
        if (SelectedProject is null || SelectedWorkspaceChange is null)
        {
            return;
        }

        var change = SelectedWorkspaceChange;
        var defaultMessage = WorkspaceOperationTextFormatter.CommitSingleFileDefaultMessage(change.Path);
        var message = TextPromptDialog.Show(
            System.Windows.Application.Current.MainWindow,
            "提交选中文件",
            defaultMessage);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var result = await WorkspaceCommitBatchRunner.CommitAsync(
                _workspaceChangeService, SelectedProject.Path,
                message, change.Path);
            StatusText = WorkspaceOperationTextFormatter.CommitSingleFileSuccess(result);
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
            WorkspaceDiffText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
        }
    }

    private async Task CommitAllWorkspaceChangesAsync()
    {
        if (SelectedProject is null || !HasSelectedWorkspaceChanges)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges();
        var paths = changes
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var message = TextPromptDialog.Show(
            System.Windows.Application.Current.MainWindow,
            "提交已选工作区变更",
            WorkspaceOperationTextFormatter.CommitMultipleDefaultMessage(paths.Count));
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var result = await WorkspaceCommitBatchRunner.CommitAsync(
                _workspaceChangeService, SelectedProject.Path,
                message, changes.Select(c => c.Change).ToList());
            StatusText = WorkspaceOperationTextFormatter.CommitMultipleSuccess(result);
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
            WorkspaceDiffText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
        }
    }

    private async Task StageSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges();
        if (changes.Count == 0)
        {
            return;
        }

        try
        {
            var result = await WorkspaceStageBatchRunner.StageAsync(
                _workspaceChangeService, SelectedProject.Path,
                changes.Select(c => c.Change).ToList());
            StatusText = $"已暂存 {result.Count} 个文件";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"暂存失败：{ex.Message}";
            WorkspaceDiffText = $"暂存失败：{ex.Message}";
        }
    }

    private async Task UnstageSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges()
            .Where(c => c.IsStaged)
            .ToList();
        if (changes.Count == 0)
        {
            StatusText = "请选择已暂存文件";
            return;
        }

        try
        {
            var result = await WorkspaceStageBatchRunner.UnstageAsync(
                _workspaceChangeService, SelectedProject.Path,
                changes.Select(c => c.Change).ToList());
            StatusText = $"已取消暂存 {result.Count} 个文件";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"取消暂存失败：{ex.Message}";
            WorkspaceDiffText = $"取消暂存失败：{ex.Message}";
        }
    }

    private void OpenWorkspaceFile()
    {
        if (SelectedWorkspaceChange is null)
        {
            return;
        }

        OpenProjectPath(SelectedWorkspaceChange.Path);
    }

    private void CopyWorkspaceDiff()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceDiffText))
        {
            return;
        }

        System.Windows.Clipboard.SetText(WorkspaceDiffText);
        StatusText = "当前 diff 已复制";
    }

    private void CopyWorkspacePath()
    {
        if (SelectedWorkspaceChange is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedWorkspaceChange.Path);
        StatusText = $"路径已复制：{SelectedWorkspaceChange.Path}";
    }

    private IReadOnlyList<WorkspaceChangeViewModel> GetCheckedWorkspaceChanges()
    {
        return WorkspaceChanges
            .Where(change => change.IsSelected)
            .ToList();
    }

    private void SetWorkspaceSelection(bool isSelected)
    {
        foreach (var change in WorkspaceChanges)
        {
            change.IsSelected = isSelected;
        }

        OnPropertyChanged(nameof(HasSelectedWorkspaceChanges));
        OnPropertyChanged(nameof(WorkspaceSelectionText));
        RaiseWorkspaceCommandStates();
    }

    private void RaiseWorkspaceCommandStates()
    {
        RefreshWorkspaceChangesCommand.RaiseCanExecuteChanged();
        RestoreWorkspaceFileCommand.RaiseCanExecuteChanged();
        CommitWorkspaceFileCommand.RaiseCanExecuteChanged();
        CommitAllWorkspaceChangesCommand.RaiseCanExecuteChanged();
        OpenWorkspaceFileCommand.RaiseCanExecuteChanged();
        CopyWorkspacePathCommand.RaiseCanExecuteChanged();
        StageSelectedWorkspaceChangesCommand.RaiseCanExecuteChanged();
        UnstageSelectedWorkspaceChangesCommand.RaiseCanExecuteChanged();
        SelectAllWorkspaceChangesCommand.RaiseCanExecuteChanged();
        ClearWorkspaceSelectionCommand.RaiseCanExecuteChanged();
    }

    private void OpenProjectPath(string relativePath)
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var root = System.IO.Path.GetFullPath(SelectedProject.Path);
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativePath));
            if (!fullPath.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "无法打开项目外路径";
                return;
            }

            var target = System.IO.File.Exists(fullPath) || System.IO.Directory.Exists(fullPath)
                ? fullPath
                : FindExistingParent(fullPath, root);
            var arguments = System.IO.File.Exists(target)
                ? $"/select,\"{target}\""
                : $"\"{target}\"";
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
            StatusText = $"已打开：{relativePath}";
        }
        catch (Exception ex)
        {
            StatusText = $"打开失败：{ex.Message}";
        }
    }

    private static string FindExistingParent(string fullPath, string root)
    {
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(directory) && directory.Length >= root.Length)
        {
            if (System.IO.Directory.Exists(directory))
            {
                return directory;
            }

            directory = System.IO.Path.GetDirectoryName(directory);
        }

        return root;
    }

    private sealed record WorkspaceRunSnapshot(string Branch, int ChangeCount, bool IsTruncated);
}
