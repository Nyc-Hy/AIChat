using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentSubAgentRunViewModel
{
    private readonly AgentSubAgentRun _run;

    public AgentSubAgentRunViewModel(AgentSubAgentRun run)
    {
        _run = run;
    }

    public string Id => _run.Id;
    public string TemplateId => _run.TemplateId;
    public string Task => _run.Task;
    public string Status => _run.Status;
    public string Summary => string.IsNullOrWhiteSpace(_run.Summary) ? _run.Status : _run.Summary;
    public string RecommendedNextStep => _run.RecommendedNextStep;
    public bool HasRecommendedNextStep => !string.IsNullOrWhiteSpace(_run.RecommendedNextStep);
    public int ToolCallCount => _run.ToolCallCount;
    public int MaxToolCalls => _run.MaxToolCalls;
    public string BudgetText => $"{ToolCallCount}/{MaxToolCalls} tools";
    public string FindingsText => _run.Findings.Count == 0
        ? "无 findings"
        : string.Join(Environment.NewLine, _run.Findings.Select(item => "- " + item));
    public string ArtifactRefsText => _run.ArtifactRefs.Count == 0
        ? "无 artifact refs"
        : string.Join(Environment.NewLine, _run.ArtifactRefs.Select(item => "- " + item));
    public string ToolCallsText => _run.ToolCalls.Count == 0
        ? "无工具调用"
        : string.Join(Environment.NewLine, _run.ToolCalls.Select(call =>
            $"- {call.ToolName}: {(call.IsError ? "error" : "ok")} {call.ResultSummary}"));
}
