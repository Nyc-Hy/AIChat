using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentRunViewModel : ObservableObject
{
    private readonly AgentRun _run;
    private ObservableCollection<AgentStepViewModel>? _steps;
    private ObservableCollection<AgentFileChangeViewModel>? _fileChanges;
    private ObservableCollection<AgentVerificationViewModel>? _verifications;
    private ObservableCollection<AgentArtifactViewModel>? _artifacts;
    private ObservableCollection<AgentSubAgentScheduleDecisionViewModel>? _subAgentScheduleDecisions;
    private ObservableCollection<AgentSubAgentRunViewModel>? _subAgentRuns;
    private ObservableCollection<AgentRunPhaseRecordViewModel>? _phaseHistory;
    private AgentPlanViewModel? _plan;

    public AgentRunViewModel(AgentRun run)
    {
        _run = run;
    }

    public AgentRun Run => _run;
    public string Id => _run.Id;
    public string Goal => _run.Goal;
    public AgentRunStatus Status => _run.Status;
    public string ProjectPath => string.IsNullOrWhiteSpace(_run.ProjectPath) ? "未记录" : _run.ProjectPath;
    public string Model => string.IsNullOrWhiteSpace(_run.Model) ? "未记录" : _run.Model;
    public int EnabledToolCount => _run.EnabledTools.Count;
    public string EnabledToolsText => _run.EnabledTools.Count == 0
        ? "无"
        : string.Join(", ", _run.EnabledTools);
    public string PermissionSummary => _run.ToolPermissionModes.Count == 0
        ? "无"
        : string.Join(Environment.NewLine, _run.ToolPermissionModes
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"- {entry.Key}: {entry.Value}"));
    public string WorkspaceSnapshotText
    {
        get
        {
            var branch = string.IsNullOrWhiteSpace(_run.WorkspaceBranch) ? "未记录分支" : _run.WorkspaceBranch;
            var dirty = _run.WorkspaceChangeCountAtStart == 0
                ? "启动时工作区干净"
                : $"启动时有 {_run.WorkspaceChangeCountAtStart} 个未提交变更";
            var truncated = _run.WorkspaceChangesWereTruncated ? "（列表被截断）" : "";
            return $"{branch} · {dirty}{truncated}";
        }
    }
    public string BudgetText
    {
        get
        {
            var rounds = _run.MaxToolRounds <= 0 ? "未记录" : _run.MaxToolRounds.ToString();
            var exhausted = _run.ToolBudgetExceeded ? " · 已耗尽" : "";
            return $"最多 {rounds} 轮工具调用 · 已调用 {_run.ToolCallCount} 次{exhausted}";
        }
    }
    public string MutationGuardrailText => _run.MutationToolSucceeded
        ? "已记录修改工具"
        : "未记录修改工具";
    public string ApprovalSummary =>
        $"需确认 {_run.ToolApprovalRequiredCount} 次 · 拒绝 {_run.ToolApprovalRejectedCount} 次 · 本会话允许 {_run.ToolSessionAllowedCount} 次";
    public string FinalValidationSummary => string.IsNullOrWhiteSpace(_run.FinalValidationSummary)
        ? "尚未生成结束校验。"
        : _run.FinalValidationSummary;
    public bool HasFinalValidationSummary => !string.IsNullOrWhiteSpace(_run.FinalValidationSummary);
    public string ExecutionPolicySummary => string.IsNullOrWhiteSpace(_run.ExecutionPolicySummary)
        ? "未记录执行策略。"
        : _run.ExecutionPolicySummary;
    public string FinalStatusReason => string.IsNullOrWhiteSpace(_run.FinalStatusReason)
        ? "未记录最终状态原因。"
        : _run.FinalStatusReason;
    public string TaskComplexityText => string.IsNullOrWhiteSpace(_run.TaskComplexity)
        ? "未记录"
        : _run.TaskComplexity;
    public string PlannerUsageText => _run.PlannerUsed ? "已启用" : "未启用";
    public string ExplorerUsageText => _run.ExplorerUsed ? "已启用" : "未启用";
    public string ExplorerDecisionReason => string.IsNullOrWhiteSpace(_run.ExplorerDecisionReason)
        ? "未记录 Explorer 决策。"
        : _run.ExplorerDecisionReason;
    public string DebugSummary =>
        $"复杂度：{TaskComplexityText}{Environment.NewLine}" +
        $"执行策略：{ExecutionPolicySummary}{Environment.NewLine}" +
        $"Planner：{PlannerUsageText}{Environment.NewLine}" +
        $"Explorer：{ExplorerUsageText} · {ExplorerDecisionReason}{Environment.NewLine}" +
        $"最终状态原因：{FinalStatusReason}";
    public string RecoverySuggestion => string.IsNullOrWhiteSpace(_run.RecoverySuggestion)
        ? $"继续处理：{Goal}"
        : _run.RecoverySuggestion;
    public bool HasRecoverySuggestion => !string.IsNullOrWhiteSpace(_run.RecoverySuggestion);
    public bool CanRetry => _run.Status is AgentRunStatus.Cancelled or AgentRunStatus.Failed;
    public bool CanContinue => _run.Status is AgentRunStatus.BudgetExceeded or AgentRunStatus.Cancelled or AgentRunStatus.Failed;
    public string ContinuedFromRunId => _run.ContinuedFromRunId;
    public bool HasContinuation => !string.IsNullOrWhiteSpace(_run.ContinuedFromRunId);
    public string ContinuedFromRunText => HasContinuation ? $"从 {ContinuedFromRunId[..Math.Min(8, ContinuedFromRunId.Length)]} 继续" : "";
    public bool CanResume => _run.Status is AgentRunStatus.BudgetExceeded or AgentRunStatus.Cancelled or AgentRunStatus.Failed &&
                             _run.Plan?.Items.Any(item => item.Status is AgentPlanItemStatus.Pending or AgentPlanItemStatus.InProgress or AgentPlanItemStatus.Blocked) == true;
    public string ShortGoal
    {
        get
        {
            var normalized = Goal.ReplaceLineEndings(" ").Trim();
            return normalized.Length <= 72 ? normalized : $"{normalized[..72]}...";
        }
    }
    public string CompletionReasonText => string.IsNullOrWhiteSpace(_run.CompletionReason)
        ? "无"
        : _run.CompletionReason;
    public bool HasCompletionReason => !string.IsNullOrWhiteSpace(_run.CompletionReason);
    public string PhaseText => _run.Phase switch
    {
        "planning" => "规划中",
        "gathering_context" => "收集上下文",
        "reading" => "收集上下文",
        "editing" => "执行中",
        "executing" => "执行中",
        "verifying" => "验证中",
        "repairing" => "修复中",
        "responding" => "生成回复",
        "summarizing" => "生成回复",
        "waiting_for_user" => "等待用户",
        "completed" => "已完成",
        "cancelled" => "已停止",
        "failed" => "失败",
        _ => "执行中"
    };
    public string StatusText => _run.Status switch
    {
        AgentRunStatus.Running => "运行中",
        AgentRunStatus.BudgetExceeded => "已暂停",
        AgentRunStatus.Cancelled => "已停止",
        AgentRunStatus.Failed => "失败",
        _ => "完成"
    };
    public string StartedText => _run.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DurationText
    {
        get
        {
            var end = _run.CompletedAt ?? DateTimeOffset.Now;
            var elapsed = end - _run.StartedAt;
            return elapsed.TotalSeconds < 1 ? "<1s" : $"{elapsed.TotalSeconds:0.0}s";
        }
    }
    public int StepCount => _run.Steps.Count;
    public int FileChangeCount => _run.FileChanges.Count;
    public int VerificationCount => _run.Verifications.Count;
    public int ArtifactCount => _run.Artifacts.Count;
    public int SubAgentScheduleDecisionCount => _run.SubAgentScheduleDecisions.Count;
    public int SubAgentRunCount => _run.SubAgentRuns.Count;
    public int PhaseHistoryCount => _run.PhaseHistory.Count;
    public string CurrentPhaseSummary => string.IsNullOrWhiteSpace(_run.CurrentPhaseSummary) ? PhaseText : _run.CurrentPhaseSummary;
    public string Summary => $"{StatusText} · {PhaseText} · {StepCount} 个步骤 · {SubAgentRunCount} 个子 Agent · {FileChangeCount} 个文件变更 · {VerificationCount} 个验证 · {ArtifactCount} 个产物 · {DurationText}";
    public ObservableCollection<AgentStepViewModel> Steps => _steps ??= new ObservableCollection<AgentStepViewModel>(
        _run.Steps.OrderBy(step => step.Number).Select(step => new AgentStepViewModel(step)));
    public ObservableCollection<AgentFileChangeViewModel> FileChanges => _fileChanges ??= new ObservableCollection<AgentFileChangeViewModel>(
        _run.FileChanges.Select(change => new AgentFileChangeViewModel(change)));
    public ObservableCollection<AgentVerificationViewModel> Verifications => _verifications ??= new ObservableCollection<AgentVerificationViewModel>(
        _run.Verifications.Select(verification => new AgentVerificationViewModel(verification)));
    public ObservableCollection<AgentArtifactViewModel> Artifacts => _artifacts ??= new ObservableCollection<AgentArtifactViewModel>(
        _run.Artifacts.Select(artifact => new AgentArtifactViewModel(artifact)));
    public ObservableCollection<AgentSubAgentScheduleDecisionViewModel> SubAgentScheduleDecisions => _subAgentScheduleDecisions ??= new ObservableCollection<AgentSubAgentScheduleDecisionViewModel>(
        _run.SubAgentScheduleDecisions.OrderBy(decision => decision.Order).Select(decision => new AgentSubAgentScheduleDecisionViewModel(decision)));
    public ObservableCollection<AgentSubAgentRunViewModel> SubAgentRuns => _subAgentRuns ??= new ObservableCollection<AgentSubAgentRunViewModel>(
        _run.SubAgentRuns.Select(subAgentRun => new AgentSubAgentRunViewModel(subAgentRun)));
    public ObservableCollection<AgentRunPhaseRecordViewModel> PhaseHistory => _phaseHistory ??= new ObservableCollection<AgentRunPhaseRecordViewModel>(
        _run.PhaseHistory.Select(record => new AgentRunPhaseRecordViewModel(record)));
    public AgentPlanViewModel? Plan
    {
        get => _run.Plan is null ? null : _plan ??= new AgentPlanViewModel(_run.Plan);
        private set => _plan = value;
    }
    public bool HasPlan => _run.Plan is not null;
    public string PlanSummary => _run.Plan is null
        ? "无计划"
        : $"{Plan!.Summary} ({Plan.ProgressText})";
    public bool HasSteps => _run.Steps.Count > 0;
    public bool HasFileChanges => _run.FileChanges.Count > 0;
    public bool HasVerifications => _run.Verifications.Count > 0;
    public bool HasArtifacts => _run.Artifacts.Count > 0;
    public bool HasSubAgentScheduleDecisions => _run.SubAgentScheduleDecisions.Count > 0;
    public bool HasSubAgentRuns => _run.SubAgentRuns.Count > 0;
    public bool HasPhaseHistory => _run.PhaseHistory.Count > 0;
    public IReadOnlyList<string> ChangedPaths => _run.FileChanges
        .Select(change => change.Path)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    public string ChangeSummary => ChangedPaths.Count == 0
        ? "本轮没有记录文件变更。"
        : string.Join(Environment.NewLine, ChangedPaths.Select(path => $"- {path}"));
    public string RunSummary => AgentRunReviewPacketBuilder.BuildRunSummary(this);
    public string ReviewPacket => AgentRunReviewPacketBuilder.BuildReviewPacket(this);

    public AgentStepViewModel AddStep(AgentStep step)
    {
        if (_run.Steps.All(item => item.Id != step.Id))
        {
            _run.Steps.Add(step);
        }

        var existing = Steps.FirstOrDefault(item => item.Id == step.Id);
        if (existing is not null)
        {
            existing.Refresh();
            return existing;
        }

        var viewModel = new AgentStepViewModel(step);
        Steps.Add(viewModel);
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
        return viewModel;
    }

    public void Complete(AgentRunStatus status, string completionReason = "")
    {
        _run.Complete(status, completionReason: completionReason);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(CurrentPhaseSummary));
        OnPropertyChanged(nameof(HasPhaseHistory));
        OnPropertyChanged(nameof(PhaseHistoryCount));
        OnPropertyChanged(nameof(FinalValidationSummary));
        OnPropertyChanged(nameof(ExecutionPolicySummary));
        OnPropertyChanged(nameof(FinalStatusReason));
        OnPropertyChanged(nameof(DebugSummary));
        OnPropertyChanged(nameof(RecoverySuggestion));
        OnPropertyChanged(nameof(HasRecoverySuggestion));
        OnPropertyChanged(nameof(CompletionReasonText));
        OnPropertyChanged(nameof(HasCompletionReason));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
        OnPropertyChanged(nameof(PlanSummary));
    }

    public void SyncFileChanges()
    {
        foreach (var change in _run.FileChanges)
        {
            if (FileChanges.Any(item => item.Id == change.Id))
            {
                continue;
            }

            FileChanges.Add(new AgentFileChangeViewModel(change));
        }

        OnPropertyChanged(nameof(HasFileChanges));
        OnPropertyChanged(nameof(FileChangeCount));
        OnPropertyChanged(nameof(ChangedPaths));
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
    }

    public void SyncVerifications()
    {
        foreach (var verification in _run.Verifications)
        {
            if (Verifications.Any(item => item.Id == verification.Id))
            {
                continue;
            }

            Verifications.Add(new AgentVerificationViewModel(verification));
        }

        OnPropertyChanged(nameof(HasVerifications));
        OnPropertyChanged(nameof(VerificationCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
    }

    public void SyncArtifacts()
    {
        foreach (var artifact in _run.Artifacts)
        {
            if (Artifacts.Any(item => item.Id == artifact.Id))
            {
                continue;
            }

            Artifacts.Add(new AgentArtifactViewModel(artifact));
        }

        OnPropertyChanged(nameof(HasArtifacts));
        OnPropertyChanged(nameof(ArtifactCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
    }

    public void SyncSubAgentRuns()
    {
        foreach (var decision in _run.SubAgentScheduleDecisions.OrderBy(item => item.Order))
        {
            if (SubAgentScheduleDecisions.Any(item => item.Id == decision.Id))
            {
                continue;
            }

            SubAgentScheduleDecisions.Add(new AgentSubAgentScheduleDecisionViewModel(decision));
        }

        foreach (var subAgentRun in _run.SubAgentRuns)
        {
            if (SubAgentRuns.Any(item => item.Id == subAgentRun.Id))
            {
                continue;
            }

            SubAgentRuns.Add(new AgentSubAgentRunViewModel(subAgentRun));
        }

        OnPropertyChanged(nameof(HasSubAgentScheduleDecisions));
        OnPropertyChanged(nameof(SubAgentScheduleDecisionCount));
        OnPropertyChanged(nameof(HasSubAgentRuns));
        OnPropertyChanged(nameof(SubAgentRunCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
    }

    public void SyncPhaseHistory()
    {
        PhaseHistory.Clear();
        foreach (var record in _run.PhaseHistory)
        {
            PhaseHistory.Add(new AgentRunPhaseRecordViewModel(record));
        }

        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(CurrentPhaseSummary));
        OnPropertyChanged(nameof(HasPhaseHistory));
        OnPropertyChanged(nameof(PhaseHistoryCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
    }

    public void SyncPlan()
    {
        if (_run.Plan is null)
        {
            return;
        }

        if (Plan is null)
        {
            Plan = new AgentPlanViewModel(_run.Plan);
        }
        else
        {
            // Sync items: update existing, add new
            foreach (var item in _run.Plan.Items)
            {
                var existing = Plan.Items.FirstOrDefault(vm => vm.Title == item.Title);
                if (existing is not null)
                {
                    existing.Refresh();
                }
                else
                {
                    Plan.Items.Add(new AgentPlanItemViewModel(item));
                }
            }
        }

        Plan?.Refresh();
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
    }
}
