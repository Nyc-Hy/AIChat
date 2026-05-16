using System.Collections.ObjectModel;
using AIChat.Application.Diagnostics;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

// UI wrapper around ChatMessage. It exposes display-friendly values while
// keeping changes synchronized back to the persisted domain model.
public sealed class ChatMessageViewModel : ObservableObject
{
    private string _content;
    private bool _isStreaming;
    private AgentRun? _agentRunSource;
    private AgentRunViewModel? _agentRun;
    private ObservableCollection<AgentFileChangeViewModel>? _agentFileChanges;

    public ChatMessageViewModel(ChatMessage message, AgentRun? agentRun = null, bool includeDetails = true)
    {
        Message = message;
        _content = message.Content;
        _agentRunSource = agentRun;
        ToolTraces = new ObservableCollection<ToolTraceViewModel>(
            includeDetails
                ? message.ToolTraces.Select(trace => new ToolTraceViewModel(trace))
                : []);
        if (includeDetails && agentRun is not null)
        {
            _agentRun = new AgentRunViewModel(agentRun);
        }
    }

    public ChatMessage Message { get; }
    public ObservableCollection<ToolTraceViewModel> ToolTraces { get; }
    public AgentRunViewModel? AgentRun
    {
        get
        {
            if (_agentRun is null && _agentRunSource is not null)
            {
                _agentRun = new AgentRunViewModel(_agentRunSource);
            }

            return _agentRun;
        }
        private set
        {
            if (SetProperty(ref _agentRun, value))
            {
                _agentRunSource = value?.Run;
                OnPropertyChanged(nameof(HasAgentRun));
                OnPropertyChanged(nameof(AgentRunStatusText));
                OnPropertyChanged(nameof(AgentRunSummaryText));
                OnPropertyChanged(nameof(AgentRunOutcomeText));
                OnPropertyChanged(nameof(AgentRunFileChangeSummaryText));
                OnPropertyChanged(nameof(AgentRunVerificationSummaryText));
                OnPropertyChanged(nameof(AgentRunRiskSummaryText));
                OnPropertyChanged(nameof(HasAgentRisk));
                OnPropertyChanged(nameof(HasAgentFileChanges));
                OnPropertyChanged(nameof(AgentFileChanges));
            }
        }
    }
    public ChatRole Role => Message.Role;
    public string Author => Role == ChatRole.User ? "你" : "AIChat";
    public string TimeText => Message.CreatedAt.ToLocalTime().ToString("HH:mm");
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public int ToolTraceCount => Message.ToolTraces.Count;
    public bool HasToolTraces => ToolTraceCount > 0;
    public bool HasAgentRun => _agentRunSource is not null || _agentRun is not null;
    public bool HasAgentFileChanges => (_agentRunSource ?? _agentRun?.Run)?.FileChanges.Count > 0;
    public ObservableCollection<AgentFileChangeViewModel> AgentFileChanges
    {
        get
        {
            var run = _agentRunSource ?? _agentRun?.Run;
            return _agentFileChanges ??= new ObservableCollection<AgentFileChangeViewModel>(
                run?.FileChanges.Select(change => new AgentFileChangeViewModel(change)) ?? []);
        }
    }
    public string AgentRunStatusText => (_agentRunSource ?? _agentRun?.Run)?.Status switch
    {
        AgentRunStatus.Running => "Agent 正在执行",
        AgentRunStatus.BudgetExceeded => "Agent 已暂停，可继续",
        AgentRunStatus.Cancelled => "Agent 已停止",
        AgentRunStatus.Failed => "Agent 执行失败",
        AgentRunStatus.Completed => "Agent 已完成",
        _ => "Agent 运行"
    };
    public string AgentRunSummaryText
    {
        get
        {
            var run = _agentRunSource ?? _agentRun?.Run;
            if (run is null)
            {
                return "";
            }

            return $"{run.Steps.Count} 个步骤 · {run.SubAgentRuns.Count} 个子 Agent · {run.FileChanges.Count} 个文件变更 · {run.Verifications.Count} 个验证 · {run.Artifacts.Count} 个产物";
        }
    }
    public string AgentRunOutcomeText => AgentRun?.CompactOutcomeText ?? "";
    public string AgentRunFileChangeSummaryText => AgentRun?.FileChangeSummaryText ?? "文件变更：无";
    public string AgentRunVerificationSummaryText => AgentRun?.VerificationSummaryText ?? "验证：未运行";
    public string AgentRunRiskSummaryText => AgentRun?.RiskSummaryText ?? "风险：无";
    public bool HasAgentRisk => AgentRun?.HasAgentRisk == true;
    public string ToolTraceSummaryText => ToolTraceCount <= 0 ? "" : $"{ToolTraceCount} 次工具调用";
    public bool IsError
    {
        get => Message.IsError;
        set
        {
            if (Message.IsError == value)
            {
                return;
            }

            Message.IsError = value;
            OnPropertyChanged();
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                // Streaming updates arrive through the ViewModel, but the domain
                // message must also update so SaveProjectsAsync persists them.
                Message.Content = value;
            }
        }
    }

    public ToolTraceViewModel AddToolTrace(ChatToolCall toolCall)
    {
        var trace = new ChatToolTrace
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.Name,
            ArgumentsJson = ToolTraceSanitizer.SanitizeArgumentsJson(toolCall.ArgumentsJson),
            StartedAt = DateTimeOffset.Now
        };
        Message.ToolTraces.Add(trace);
        var viewModel = new ToolTraceViewModel(trace);
        ToolTraces.Add(viewModel);
        OnPropertyChanged(nameof(HasToolTraces));
        OnPropertyChanged(nameof(ToolTraceCount));
        OnPropertyChanged(nameof(ToolTraceSummaryText));
        return viewModel;
    }

    public ToolTraceViewModel? FindToolTrace(string toolCallId)
    {
        return ToolTraces.FirstOrDefault(trace =>
            string.Equals(trace.ToolCallId, toolCallId, StringComparison.Ordinal));
    }

    public void AttachAgentRun(AgentRun run)
    {
        Message.AgentRunId = run.Id;
        _agentRunSource = run;
        _agentFileChanges = null;
        AgentRun = new AgentRunViewModel(run);
    }

    public AgentStepViewModel? AddAgentStep(AgentStep step)
    {
        var run = AgentRun;
        if (run is null)
        {
            return null;
        }

        var viewModel = run.AddStep(step);
        OnPropertyChanged(nameof(HasAgentRun));
        OnPropertyChanged(nameof(AgentRunOutcomeText));
        OnPropertyChanged(nameof(AgentRunSummaryText));
        return viewModel;
    }

    public void SyncAgentFileChanges()
    {
        _agentFileChanges = null;
        AgentRun?.SyncFileChanges();
        OnPropertyChanged(nameof(HasAgentFileChanges));
        OnPropertyChanged(nameof(AgentFileChanges));
        OnPropertyChanged(nameof(AgentRunSummaryText));
        OnPropertyChanged(nameof(AgentRunFileChangeSummaryText));
    }

    public void SyncAgentVerifications()
    {
        AgentRun?.SyncVerifications();
        OnPropertyChanged(nameof(AgentRunVerificationSummaryText));
        OnPropertyChanged(nameof(AgentRunRiskSummaryText));
        OnPropertyChanged(nameof(HasAgentRisk));
        OnPropertyChanged(nameof(AgentRunSummaryText));
    }

    public void SyncAgentArtifacts()
    {
        AgentRun?.SyncArtifacts();
        OnPropertyChanged(nameof(AgentRunSummaryText));
    }

    public void SyncSubAgentRuns()
    {
        AgentRun?.SyncSubAgentRuns();
        OnPropertyChanged(nameof(AgentRunSummaryText));
    }

    public void SyncAgentPhase()
    {
        AgentRun?.SyncPhaseHistory();
        OnPropertyChanged(nameof(AgentRunOutcomeText));
        OnPropertyChanged(nameof(AgentRunSummaryText));
    }

    public void SyncAgentPlan()
    {
        AgentRun?.SyncPlan();
    }
}
