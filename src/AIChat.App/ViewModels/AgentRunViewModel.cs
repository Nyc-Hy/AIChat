using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentRunViewModel : ObservableObject
{
    private readonly AgentRun _run;

    public AgentRunViewModel(AgentRun run)
    {
        _run = run;
        Steps = new ObservableCollection<AgentStepViewModel>(
            run.Steps.OrderBy(step => step.Number).Select(step => new AgentStepViewModel(step)));
        FileChanges = new ObservableCollection<AgentFileChangeViewModel>(
            run.FileChanges.Select(change => new AgentFileChangeViewModel(change)));
        Verifications = new ObservableCollection<AgentVerificationViewModel>(
            run.Verifications.Select(verification => new AgentVerificationViewModel(verification)));
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
    public string MutationGuardrailText => _run.RequiresProjectMutation
        ? _run.MutationToolSucceeded
            ? "需要项目修改 · 已记录修改工具"
            : "需要项目修改 · 未记录修改工具"
        : "未识别为项目修改任务";
    public string ApprovalSummary =>
        $"需确认 {_run.ToolApprovalRequiredCount} 次 · 拒绝 {_run.ToolApprovalRejectedCount} 次 · 本会话允许 {_run.ToolSessionAllowedCount} 次";
    public string FinalValidationSummary => string.IsNullOrWhiteSpace(_run.FinalValidationSummary)
        ? "尚未生成结束校验。"
        : _run.FinalValidationSummary;
    public string RecoverySuggestion => string.IsNullOrWhiteSpace(_run.RecoverySuggestion)
        ? $"继续处理：{Goal}"
        : _run.RecoverySuggestion;
    public bool HasRecoverySuggestion => !string.IsNullOrWhiteSpace(_run.RecoverySuggestion);
    public bool CanRetry => _run.Status is AgentRunStatus.Cancelled or AgentRunStatus.Failed;
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
        "reading" => "读取上下文",
        "editing" => "修改文件",
        "verifying" => "验证中",
        "responding" => "生成回复",
        "completed" => "已完成",
        "cancelled" => "已停止",
        "failed" => "失败",
        _ => "执行中"
    };
    public string StatusText => _run.Status switch
    {
        AgentRunStatus.Running => "运行中",
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
    public int StepCount => Steps.Count;
    public int FileChangeCount => FileChanges.Count;
    public int VerificationCount => Verifications.Count;
    public string Summary => $"{StatusText} · {StepCount} 个步骤 · {FileChangeCount} 个文件变更 · {VerificationCount} 个验证 · {DurationText}";
    public ObservableCollection<AgentStepViewModel> Steps { get; }
    public ObservableCollection<AgentFileChangeViewModel> FileChanges { get; }
    public ObservableCollection<AgentVerificationViewModel> Verifications { get; }
    public bool HasSteps => Steps.Count > 0;
    public bool HasFileChanges => FileChanges.Count > 0;
    public bool HasVerifications => Verifications.Count > 0;
    public IReadOnlyList<string> ChangedPaths => FileChanges
        .Select(change => change.Path)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    public string ChangeSummary => ChangedPaths.Count == 0
        ? "本轮没有记录文件变更。"
        : string.Join(Environment.NewLine, ChangedPaths.Select(path => $"- {path}"));
    public string RunSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"状态：{StatusText}",
                $"阶段：{PhaseText}",
                $"目标：{Goal}",
                $"项目：{ProjectPath}",
                $"模型：{Model}",
                $"工具：{EnabledToolCount}",
                $"预算：{BudgetText}",
                $"修改护栏：{MutationGuardrailText}",
                $"审批：{ApprovalSummary}",
                $"工作区：{WorkspaceSnapshotText}",
                $"步骤：{StepCount}",
                $"文件变更：{FileChangeCount}",
                $"验证：{VerificationCount}",
                $"耗时：{DurationText}"
            };

            if (HasCompletionReason)
            {
                lines.Add($"原因：{CompletionReasonText}");
            }

            if (!string.IsNullOrWhiteSpace(_run.FinalValidationSummary))
            {
                lines.Add("");
                lines.Add("结束校验：");
                lines.Add(_run.FinalValidationSummary);
            }

            if (!string.IsNullOrWhiteSpace(_run.RecoverySuggestion))
            {
                lines.Add("");
                lines.Add("恢复建议：");
                lines.Add(_run.RecoverySuggestion);
            }

            if (ChangedPaths.Count > 0)
            {
                lines.Add("");
                lines.Add("变更文件：");
                lines.AddRange(ChangedPaths.Select(path => $"- {path}"));
            }

            if (Verifications.Count > 0)
            {
                lines.Add("");
                lines.Add("验证结果：");
                lines.AddRange(Verifications.Select(item => $"- {item.Command}: {item.StatusText} ({item.ExitCodeText})"));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
    public string ReviewPacket
    {
        get
        {
            var lines = new List<string>
            {
                "# Agent Run Review",
                "",
                $"Status: {StatusText}",
                $"Goal: {Goal}",
                $"Model: {Model}",
                $"Project: {ProjectPath}",
                $"Started: {StartedText}",
                $"Duration: {DurationText}",
                "",
                "## Guardrails",
                $"- Budget: {BudgetText}",
                $"- Mutation: {MutationGuardrailText}",
                $"- Approval: {ApprovalSummary}",
                $"- Workspace: {WorkspaceSnapshotText}",
                "",
                "## Final Validation",
                FinalValidationSummary,
                "",
                "## Recovery Suggestion",
                RecoverySuggestion
            };

            if (HasCompletionReason)
            {
                lines.Add("");
                lines.Add("## Completion Reason");
                lines.Add(CompletionReasonText);
            }

            if (ChangedPaths.Count > 0)
            {
                lines.Add("");
                lines.Add("## Changed Files");
                lines.AddRange(ChangedPaths.Select(path => $"- {path}"));
            }

            if (Verifications.Count > 0)
            {
                lines.Add("");
                lines.Add("## Verifications");
                lines.AddRange(Verifications.Select(item => $"- {item.Command}: {item.StatusText} ({item.ExitCodeText})"));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

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
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(FinalValidationSummary));
        OnPropertyChanged(nameof(RecoverySuggestion));
        OnPropertyChanged(nameof(HasRecoverySuggestion));
        OnPropertyChanged(nameof(CompletionReasonText));
        OnPropertyChanged(nameof(HasCompletionReason));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(ReviewPacket));
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
}
