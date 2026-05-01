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

    public string Id => _run.Id;
    public string Goal => _run.Goal;
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
                $"步骤：{StepCount}",
                $"文件变更：{FileChangeCount}",
                $"验证：{VerificationCount}",
                $"耗时：{DurationText}"
            };

            if (HasCompletionReason)
            {
                lines.Add($"原因：{CompletionReasonText}");
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
        return viewModel;
    }

    public void Complete(AgentRunStatus status, string completionReason = "")
    {
        _run.Complete(status, completionReason: completionReason);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(CompletionReasonText));
        OnPropertyChanged(nameof(HasCompletionReason));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunSummary));
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
    }
}
