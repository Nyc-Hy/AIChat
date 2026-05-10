using System.Windows;
using AIChat.App.Controls;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsAgentRunHistoryOpen
    {
        get => _isAgentRunHistoryOpen;
        set => SetProperty(ref _isAgentRunHistoryOpen, value);
    }

    public bool IsAgentRunDetailsOpen
    {
        get => _isAgentRunDetailsOpen;
        set => SetProperty(ref _isAgentRunDetailsOpen, value);
    }

    public AgentRunHistoryItemViewModel? SelectedAgentRunHistoryItem
    {
        get => _selectedAgentRunHistoryItem;
        private set => SetProperty(ref _selectedAgentRunHistoryItem, value);
    }

    public bool HasAgentRunHistory => AgentRunHistory.Count > 0;
    public string AgentRunHistoryFilterId
    {
        get => _agentRunHistoryFilterId;
        set
        {
            if (SetProperty(ref _agentRunHistoryFilterId, string.IsNullOrWhiteSpace(value) ? "all" : value))
            {
                RebuildAgentRunHistoryIfOpen();
            }
        }
    }
    public string AgentRunHistoryTitle => SelectedConversation is null
        ? "运行历史"
        : $"{SelectedConversation.Title} · 运行历史";
    public string AgentRunHistorySummary => AgentRunHistory.Count == 0
        ? _agentRunHistoryTotalCount == 0
            ? "暂无 Agent 运行记录"
            : $"当前筛选无匹配 · 总计 {_agentRunHistoryTotalCount} 次运行"
        : $"显示 {AgentRunHistory.Count} / {_agentRunHistoryTotalCount} 次运行 · {AgentRunHistory.Count(item => item.CanRetry)} 个可重试";

    public AgentRunViewModel? SelectedAgentRunDetails
    {
        get => _selectedAgentRunDetails;
        private set
        {
            if (SetProperty(ref _selectedAgentRunDetails, value))
            {
                OnPropertyChanged(nameof(AgentRunDetailsTitle));
                CopySelectedAgentRunSummaryCommand.RaiseCanExecuteChanged();
                CopySelectedAgentRunReviewPacketCommand.RaiseCanExecuteChanged();
                RetrySelectedAgentRunCommand.RaiseCanExecuteChanged();
                ContinueSelectedAgentRunCommand.RaiseCanExecuteChanged();
                _ = LoadAuditEventsAsync(value);
            }
        }
    }

    public string AgentRunDetailsTitle => SelectedAgentRunDetails is null
        ? "Agent Run"
        : $"Agent Run · {SelectedAgentRunDetails.StatusText}";

    public bool HasAuditEvents => AuditEvents.Count > 0;

    private async Task LoadAuditEventsAsync(AgentRunViewModel? run)
    {
        await InvokeOnUiAsync(() =>
        {
            AuditEvents.Clear();
            OnPropertyChanged(nameof(HasAuditEvents));
        });

        var projectId = SelectedProject?.Project.Id ?? "";
        var runId = run?.Id ?? "";
        if (run is null || _auditService?.IsAvailable != true || string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        try
        {
            var items = await _auditService.LoadRunEventsAsync(projectId, runId, run.Run.StartedAt);

            if (SelectedAgentRunDetails?.Id != runId)
            {
                return;
            }

            await InvokeOnUiAsync(() =>
            {
                foreach (var e in items)
                {
                    AuditEvents.Add(new AuditEventViewModel(e));
                }

                OnPropertyChanged(nameof(HasAuditEvents));
            });
        }
        catch
        {
            // Audit display is best-effort; don't break the UI.
        }
    }

    private void RebuildAgentRunHistory()
    {
        AgentRunHistory.Clear();
        if (SelectedConversation is null)
        {
            _agentRunHistoryTotalCount = 0;
            RaiseAgentRunHistoryProperties();
            return;
        }

        var allItems = AgentRunHistoryFilter.GatherFromConversation(SelectedConversation);

        _agentRunHistoryTotalCount = allItems.Count;
        var items = FilterAgentRunHistory(allItems).ToList();

        foreach (var item in items)
        {
            AgentRunHistory.Add(item);
        }

        RaiseAgentRunHistoryProperties();
    }

    private void RebuildAgentRunHistoryIfOpen()
    {
        if (IsAgentRunHistoryOpen)
        {
            RebuildAgentRunHistory();
        }
    }

    private void RaiseAgentRunHistoryProperties()
    {
        OnPropertyChanged(nameof(HasAgentRunHistory));
        OnPropertyChanged(nameof(AgentRunHistorySummary));
        RetryAgentRunCommand.RaiseCanExecuteChanged();
        ContinueAgentRunCommand.RaiseCanExecuteChanged();
    }

    private IEnumerable<AgentRunHistoryItemViewModel> FilterAgentRunHistory(IEnumerable<AgentRunHistoryItemViewModel> items)
    {
        return AgentRunHistoryFilter.Apply(items, AgentRunHistoryFilterId);
    }

    private void OpenAgentRunDetails(ChatMessageViewModel message)
    {
        if (message.AgentRun is null)
        {
            return;
        }

        SelectedAgentRunDetails = message.AgentRun;
        IsAgentRunDetailsOpen = true;
    }

    private void OpenAgentRunHistory()
    {
        RebuildAgentRunHistory();
        IsAgentRunHistoryOpen = true;
    }

    private void SelectAgentRunHistoryItem(AgentRunHistoryItemViewModel item)
    {
        SelectedAgentRunHistoryItem = item;
        SelectedAgentRunDetails = item.Run;
        IsAgentRunDetailsOpen = true;
    }

    private void RetryAgentRun(AgentRunHistoryItemViewModel item)
    {
        if (!item.CanRetry || IsSending)
        {
            return;
        }

        SelectConversation(item.Conversation);
        DraftMessage = item.Run.RecoverySuggestion;
        IsAgentRunHistoryOpen = false;
        IsAgentRunDetailsOpen = false;
        StatusText = "已把恢复建议放回输入框，可检查后重新发送";
    }

    private void RetrySelectedAgentRun()
    {
        var selected = SelectedAgentRunDetails;
        if (selected is null || !selected.CanRetry || IsSending)
        {
            return;
        }

        var historyItem = AgentRunHistory.FirstOrDefault(item => item.Run.Id == selected.Id);
        if (historyItem is not null)
        {
            RetryAgentRun(historyItem);
            return;
        }

        DraftMessage = selected.RecoverySuggestion;
        IsAgentRunDetailsOpen = false;
        StatusText = "已把恢复建议放回输入框，可检查后重新发送";
    }

    private async Task ContinueAgentRunAsync(AgentRunHistoryItemViewModel item)
    {
        if (!item.CanContinue || IsSending)
        {
            return;
        }

        SelectConversation(item.Conversation);
        IsAgentRunHistoryOpen = false;
        IsAgentRunDetailsOpen = false;
        DraftMessage = item.Run.RecoverySuggestion;
        _pendingContinuedFromRunId = item.Run.Id;
        await SendAsync();
    }

    private async Task ContinueSelectedAgentRunAsync()
    {
        var selected = SelectedAgentRunDetails;
        if (selected is null || !selected.CanContinue || IsSending)
        {
            return;
        }

        var historyItem = AgentRunHistory.FirstOrDefault(item => item.Run.Id == selected.Id);
        if (historyItem is not null)
        {
            await ContinueAgentRunAsync(historyItem);
            return;
        }

        IsAgentRunDetailsOpen = false;
        DraftMessage = selected.RecoverySuggestion;
        _pendingContinuedFromRunId = selected.Id;
        await SendAsync();
    }

    private string _pendingContinuedFromRunId = "";

    private void OpenAgentFileChange(AgentFileChangeViewModel change)
    {
        OpenProjectPath(change.Path);
    }

    private void CopyAgentFileDiff(AgentFileChangeViewModel change)
    {
        if (!change.HasDiff)
        {
            return;
        }

        System.Windows.Clipboard.SetText(change.DiffText);
        StatusText = $"已复制 diff：{change.Path}";
    }

    private void CopyAgentFilePath(AgentFileChangeViewModel change)
    {
        System.Windows.Clipboard.SetText(change.Path);
        StatusText = $"路径已复制：{change.Path}";
    }

    private void CopyTrace(ToolTraceViewModel trace)
    {
        System.Windows.Clipboard.SetText(trace.GetFullText());
        StatusText = $"工具调用详情已复制：{trace.ToolName}";
    }

    private async Task CommitAgentRunChangesAsync(ChatMessageViewModel message)
    {
        if (SelectedProject is null || message.AgentRun is null)
        {
            return;
        }

        var paths = message.AgentRun.ChangedPaths;
        if (paths.Count == 0)
        {
            return;
        }

        var defaultMessage = $"Update agent changes ({paths.Count} files)";
        var commitMessage = TextPromptDialog.Show(
            System.Windows.Application.Current.MainWindow,
            "提交本轮变更",
            defaultMessage);
        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            return;
        }

        try
        {
            var result = await _workspaceChangeService.CommitAsync(
                SelectedProject.Path,
                commitMessage,
                paths);
            StatusText = string.IsNullOrWhiteSpace(result.Commit)
                ? $"已提交本轮 {result.Paths.Count} 个文件：{result.Message}"
                : $"已提交本轮 {result.Commit}：{result.Message}";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"提交本轮失败：{ex.Message}";
            WorkspaceDiffText = $"提交本轮失败：{ex.Message}";
        }
    }

    private async Task RestoreAgentRunChangesAsync(ChatMessageViewModel message)
    {
        if (SelectedProject is null || message.AgentRun is null)
        {
            return;
        }

        var fileChanges = message.AgentRun.FileChanges.ToList();
        if (fileChanges.Count == 0)
        {
            return;
        }

        // Detect conflicts: files that were manually edited after the Agent run
        var conflicts = new List<string>();
        foreach (var change in fileChanges)
        {
            if (string.IsNullOrEmpty(change.PostChangeHash))
            {
                continue;
            }

            var fullPath = System.IO.Path.Combine(SelectedProject.Path, change.Path);
            if (!System.IO.File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                var currentContent = await System.IO.File.ReadAllTextAsync(fullPath);
                var currentHash = ComputeContentHash(currentContent);
                if (currentHash != change.PostChangeHash)
                {
                    conflicts.Add(change.Path);
                }
            }
            catch
            {
                // Can't read file, skip conflict check for this file
            }
        }

        var confirmMessage = conflicts.Count > 0
            ? $"以下 {conflicts.Count} 个文件在 Agent 修改后又被手动编辑过：\n\n{string.Join("\n", conflicts.Select(p => "  - " + p))}\n\n仍要撤销这些文件的变更吗？手动编辑的内容将丢失。"
            : $"撤销本轮记录的 {fileChanges.Count} 个文件变更？\n\n这会恢复已跟踪文件，并删除本轮创建后仍未跟踪的文件。";

        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            confirmMessage,
            "确认撤销本轮变更",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var restored = 0;
        var errors = new List<string>();
        foreach (var change in fileChanges)
        {
            try
            {
                var result = await _workspaceChangeService.RestoreFileAsync(
                    SelectedProject.Path,
                    change.Path,
                    deleteUntracked: true);
                restored++;
            }
            catch
            {
                // git restore failed — try snapshot-based fallback for untracked files
                if (!string.IsNullOrEmpty(change.ContentSnapshot))
                {
                    try
                    {
                        var fullPath = System.IO.Path.Combine(SelectedProject.Path, change.Path);
                        var directory = System.IO.Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(directory) && !System.IO.Directory.Exists(directory))
                        {
                            System.IO.Directory.CreateDirectory(directory);
                        }

                        await System.IO.File.WriteAllTextAsync(fullPath, change.ContentSnapshot);
                        restored++;
                    }
                    catch (Exception ex2)
                    {
                        errors.Add($"{change.Path}: {ex2.Message}");
                    }
                }
                else
                {
                    errors.Add($"{change.Path}: 无法恢复");
                }
            }
        }

        StatusText = errors.Count == 0
            ? $"已撤销本轮 {restored} 个文件变更"
            : $"已撤销 {restored} 个文件，{errors.Count} 个失败";
        if (errors.Count > 0)
        {
            WorkspaceDiffText = string.Join(Environment.NewLine, errors);
        }

        await RefreshWorkspaceChangesAsync();
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void CopyAgentRunChangeSummary(ChatMessageViewModel message)
    {
        if (message.AgentRun is null || message.AgentRun.ChangedPaths.Count == 0)
        {
            return;
        }

        System.Windows.Clipboard.SetText(message.AgentRun.ChangeSummary);
        StatusText = "本轮变更摘要已复制";
    }

    private void CopySelectedAgentRunSummary()
    {
        if (SelectedAgentRunDetails is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedAgentRunDetails.RunSummary);
        StatusText = "Agent Run 摘要已复制";
    }

    private void CopySelectedAgentRunReviewPacket()
    {
        if (SelectedAgentRunDetails is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedAgentRunDetails.ReviewPacket);
        StatusText = "Agent Run 复盘包已复制";
    }

    private static bool CanOperateAgentRunChanges(object? parameter)
    {
        return parameter is ChatMessageViewModel { AgentRun.HasFileChanges: true };
    }
}
