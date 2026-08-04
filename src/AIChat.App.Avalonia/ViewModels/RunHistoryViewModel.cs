using System.Collections.ObjectModel;
using AIChat.Domain.Chat;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Focused run observability for the current project. AgentRun already stores
// the facts; this view-model turns them into a browse/filter/detail surface
// without inventing provider usage numbers that were never returned.
public sealed partial class RunHistoryViewModel : ViewModelBase
{
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly Action<RunHistoryItemViewModel> _retry;
    private readonly Action<RunHistoryItemViewModel> _continue;
    private List<RunHistoryItemViewModel> _allRuns = [];

    public ObservableCollection<string> StatusFilters { get; } =
        ["全部", "完成", "失败", "已停止", "预算暂停", "运行中"];

    public ObservableCollection<RunHistoryItemViewModel> Runs { get; } = [];

    [ObservableProperty]
    private string selectedStatusFilter = "全部";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RetrySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueSelectedCommand))]
    private RunHistoryItemViewModel? selectedRun;

    public bool HasRuns => Runs.Count > 0;
    public bool HasSelection => SelectedRun is not null;
    public string RunCountText => $"{Runs.Count} 次运行";
    public string EmptyStateMessage => _sidebar.CurrentProject is null
        ? "请先选择项目。"
        : SelectedStatusFilter == "全部"
            ? "这个项目还没有运行记录。"
            : $"没有“{SelectedStatusFilter}”状态的运行。";

    public RunHistoryViewModel(
        ProjectSidebarViewModel sidebar,
        Action<RunHistoryItemViewModel> retry,
        Action<RunHistoryItemViewModel> @continue)
    {
        _sidebar = sidebar;
        _retry = retry;
        _continue = @continue;
    }

    public void Refresh()
    {
        // Wave 2: sessions 是外部 store,sidebar 在 ApplyProject 时已经 load 好。
        // Standalone sessions 不属于任何 project,这里只显示绑到当前项目的。
        _allRuns = _sidebar.CurrentProjectSessions
            .SelectMany(session => session.AgentRuns.Select(run =>
                new RunHistoryItemViewModel(session.Id, session.Title, run)))
            .OrderByDescending(item => item.Run.StartedAt)
            .ToList();
        ApplyFilter();
    }

    partial void OnSelectedStatusFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var previousId = SelectedRun?.Run.Id;
        Runs.Clear();
        foreach (var run in _allRuns.Where(run =>
                     SelectedStatusFilter == "全部" || run.StatusDisplay == SelectedStatusFilter))
        {
            Runs.Add(run);
        }

        SelectedRun = Runs.FirstOrDefault(run => run.Run.Id == previousId) ?? Runs.FirstOrDefault();
        OnPropertyChanged(nameof(HasRuns));
        OnPropertyChanged(nameof(RunCountText));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private bool CanActOnSelected() =>
        SelectedRun is { Run.Status: not AgentRunStatus.Running };

    [RelayCommand(CanExecute = nameof(CanActOnSelected))]
    private void RetrySelected()
    {
        if (SelectedRun is not null)
        {
            _retry(SelectedRun);
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelected))]
    private void ContinueSelected()
    {
        if (SelectedRun is not null)
        {
            _continue(SelectedRun);
        }
    }
}

public sealed class RunHistoryItemViewModel
{
    public AgentRun Run { get; }
    public string ConversationId { get; }
    public string ConversationTitle { get; }
    public string Goal => string.IsNullOrWhiteSpace(Run.Goal) ? "(未记录任务目标)" : Run.Goal;
    public string StatusDisplay => FormatStatus(Run.Status);
    // 1.0.1: per-row status color, matching
    // the Codex-style "colored dot per
    // state" visual that the panel's
    // sub-agent rows already use. A
    // user skimming the run history
    // list (often 20+ rows after a
    // week of daily driving) can
    // spot the 3 failures without
    // reading the status text.
    public IBrush StatusBrush => FormatStatusBrush(Run.Status);
    public string StartedAtDisplay => Run.StartedAt.ToLocalTime().ToString("M月d日 HH:mm");
    public string DurationDisplay => Run.CompletedAt is { } completed
        ? FormatDuration(completed - Run.StartedAt)
        : "仍在运行";
    public string ModelDisplay => string.IsNullOrWhiteSpace(Run.Model) ? "未记录" : Run.Model;
    public string ContextDisplay => Run.ContextEstimatedTokens > 0
        ? $"约 {Run.ContextEstimatedTokens:N0} tokens（估算）"
        : "未记录";
    public string MetricsDisplay =>
        $"模型 {Run.ModelCallCount} 轮 · 工具 {Run.ToolCallCount} 次 · 文件 {Run.FileChanges.Count} 个 · 子 Agent {Run.SubAgentRuns.Count} 个";
    public string VerificationDisplay => Run.Verifications.Count == 0
        ? "未运行"
        : $"{Run.Verifications.Count(item => item.IsSuccess)}/{Run.Verifications.Count} 通过";
    public string CompletionReasonDisplay => string.IsNullOrWhiteSpace(Run.CompletionReason)
        ? "未记录"
        : Run.CompletionReason;
    public string FileChangesDisplay => Run.FileChanges.Count == 0
        ? "没有记录到文件变更"
        : string.Join("\n", Run.FileChanges.Take(12).Select(change => change.Path)) +
          (Run.FileChanges.Count > 12 ? $"\n… 另有 {Run.FileChanges.Count - 12} 个文件" : "");
    public string LineageDisplay => !string.IsNullOrWhiteSpace(Run.RetriedFromRunId)
        ? $"重试自 {ShortId(Run.RetriedFromRunId)}"
        : !string.IsNullOrWhiteSpace(Run.ContinuedFromRunId)
            ? $"继续自 {ShortId(Run.ContinuedFromRunId)}"
            : "首次运行";
    public string UsageNote => "上下文为本地估算；提供方 input / output / cache usage 未返回时不显示推测值。";

    public RunHistoryItemViewModel(string conversationId, string conversationTitle, AgentRun run)
    {
        ConversationId = string.IsNullOrWhiteSpace(run.ConversationId)
            ? conversationId
            : run.ConversationId;
        ConversationTitle = string.IsNullOrWhiteSpace(conversationTitle) ? "未命名对话" : conversationTitle;
        Run = run;
    }

    private static string FormatStatus(AgentRunStatus status) => status switch
    {
        AgentRunStatus.Completed => "完成",
        AgentRunStatus.Failed => "失败",
        AgentRunStatus.Cancelled => "已停止",
        AgentRunStatus.BudgetExceeded => "预算暂停",
        _ => "运行中"
    };

    // Match the same palette the
    // SubAgentRunViewModel uses, so
    // a "Completed" sub-agent row
    // and a "Completed" run-history
    // row read as the same shade of
    // green. SolidColorBrush literals
    // are fine here — the XAML is
    // already using SolidColorBrush
    // for the sub-agent dots.
    private static IBrush FormatStatusBrush(AgentRunStatus status) => status switch
    {
        AgentRunStatus.Completed => new SolidColorBrush(Color.Parse("#5cd6a8")),
        AgentRunStatus.Failed => new SolidColorBrush(Color.Parse("#ff6b6b")),
        AgentRunStatus.Cancelled => new SolidColorBrush(Color.Parse("#f5a623")),
        AgentRunStatus.BudgetExceeded => new SolidColorBrush(Color.Parse("#9aa0a6")),
        _ => new SolidColorBrush(Color.Parse("#9aa0a6"))
    };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1) return "<1s";
        if (duration.TotalMinutes < 1) return $"{(int)duration.TotalSeconds}s";
        return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
    }

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];
}
