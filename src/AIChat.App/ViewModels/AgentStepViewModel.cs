using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentStepViewModel : ObservableObject
{
    private readonly AgentStep _step;

    public AgentStepViewModel(AgentStep step)
    {
        _step = step;
    }

    public string Id => _step.Id;
    public string ToolCallId => _step.ToolCallId;
    public string Title => _step.Title;
    public string Input => _step.Input;
    public string Output => _step.Output;
    public bool HasInput => !string.IsNullOrWhiteSpace(_step.Input);
    public bool HasOutput => !string.IsNullOrWhiteSpace(_step.Output);
    public string OutputPreview => string.IsNullOrWhiteSpace(_step.Output)
        ? ""
        : _step.Output.Length > 1_200 ? _step.Output[..1_200] + "\n..." : _step.Output;
    public string Subtitle => _step.Type switch
    {
        AgentStepType.ToolCall => string.IsNullOrWhiteSpace(_step.ToolName) ? "工具调用" : _step.ToolName,
        AgentStepType.ToolResult => _step.IsError ? "工具失败" : "工具完成",
        AgentStepType.Approval => "权限确认",
        AgentStepType.Final => "最终回复",
        _ => "模型步骤"
    };
    public string StatusText => _step.Status switch
    {
        AgentStepStatus.Running => "运行中",
        AgentStepStatus.Rejected => "已拒绝",
        AgentStepStatus.Failed => "失败",
        _ => "完成"
    };
    public string DurationText
    {
        get
        {
            var end = _step.CompletedAt ?? DateTimeOffset.Now;
            var elapsed = end - _step.StartedAt;
            return elapsed.TotalSeconds < 1 ? "<1s" : $"{elapsed.TotalSeconds:0.0}s";
        }
    }
    public bool IsError => _step.IsError || _step.Status == AgentStepStatus.Failed;

    public void Complete(string output, bool isError)
    {
        _step.Output = output;
        _step.IsError = isError;
        _step.Status = isError ? AgentStepStatus.Failed : AgentStepStatus.Completed;
        _step.CompletedAt = DateTimeOffset.Now;
        RaiseAll();
    }

    public void Refresh()
    {
        RaiseAll();
    }

    public void MarkRejected(string output)
    {
        _step.Output = output;
        _step.IsError = true;
        _step.Status = AgentStepStatus.Rejected;
        _step.CompletedAt = DateTimeOffset.Now;
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(Output));
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(OutputPreview));
    }
}
