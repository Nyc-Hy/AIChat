using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentSubAgentScheduleDecisionViewModel
{
    private readonly AgentSubAgentScheduleDecision _decision;

    public AgentSubAgentScheduleDecisionViewModel(AgentSubAgentScheduleDecision decision)
    {
        _decision = decision;
    }

    public string Id => _decision.Id;
    public string TemplateId => _decision.TemplateId;
    public string Task => string.IsNullOrWhiteSpace(_decision.Task) ? "自动上下文收集" : _decision.Task;
    public string Status => _decision.Status;
    public string Reason => string.IsNullOrWhiteSpace(_decision.SkipReason) ? _decision.Reason : _decision.SkipReason;
    public string Phase => _decision.Phase;
    public string BudgetText => $"{_decision.MaxToolCalls} tools";
    public string DependsOnText => _decision.DependsOn.Count == 0 ? "无依赖" : string.Join(", ", _decision.DependsOn);
    public bool IsSkipped => string.Equals(_decision.Status, "Skipped", StringComparison.OrdinalIgnoreCase);
}
