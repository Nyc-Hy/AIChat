using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

// UI wrapper around ChatMessage. It exposes display-friendly values while
// keeping changes synchronized back to the persisted domain model.
public sealed class ChatMessageViewModel : ObservableObject
{
    private string _content;
    private bool _isStreaming;
    private AgentRunViewModel? _agentRun;

    public ChatMessageViewModel(ChatMessage message, AgentRun? agentRun = null)
    {
        Message = message;
        _content = message.Content;
        ToolTraces = new ObservableCollection<ToolTraceViewModel>(
            message.ToolTraces.Select(trace => new ToolTraceViewModel(trace)));
        if (agentRun is not null)
        {
            _agentRun = new AgentRunViewModel(agentRun);
        }
    }

    public ChatMessage Message { get; }
    public ObservableCollection<ToolTraceViewModel> ToolTraces { get; }
    public AgentRunViewModel? AgentRun
    {
        get => _agentRun;
        private set
        {
            if (SetProperty(ref _agentRun, value))
            {
                OnPropertyChanged(nameof(HasAgentRun));
            }
        }
    }
    public ChatRole Role => Message.Role;
    public string Author => Role == ChatRole.User ? "你" : "AIChat";
    public string TimeText => Message.CreatedAt.ToLocalTime().ToString("HH:mm");
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool HasToolTraces => ToolTraces.Count > 0;
    public bool HasAgentRun => AgentRun?.HasSteps == true;
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
            ArgumentsJson = string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson,
            StartedAt = DateTimeOffset.Now
        };
        Message.ToolTraces.Add(trace);
        var viewModel = new ToolTraceViewModel(trace);
        ToolTraces.Add(viewModel);
        OnPropertyChanged(nameof(HasToolTraces));
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
        return viewModel;
    }
}
