namespace AIChat.Domain.Chat;

public sealed class AgentStructuredPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool IsFallback { get; set; }
    public List<AgentPlanPhase> Phases { get; set; } = [];
    public List<string> SuggestedTools { get; set; } = [];
    public List<string> SuggestedContext { get; set; } = [];
    public AgentPlanBudget Budget { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public AgentPlan ToAgentPlan()
    {
        var items = Phases
            .SelectMany(phase => phase.Tasks.Select(task => (Phase: phase, Task: task)))
            .OrderBy(item => item.Task.Order)
            .Select((item, index) => new AgentPlanItem
            {
                Title = $"[{item.Phase.Name}] {item.Task.Title}",
                Status = index == 0 ? AgentPlanItemStatus.InProgress : AgentPlanItemStatus.Pending,
                Notes = BuildNotes(item.Phase, item.Task),
                Order = index
            })
            .ToList();

        return new AgentPlan
        {
            RunId = RunId,
            Summary = Summary,
            Items = items,
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        };
    }

    private static string BuildNotes(AgentPlanPhase phase, AgentPlanTask task)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(phase.Objective))
        {
            parts.Add($"阶段目标：{phase.Objective}");
        }

        if (!string.IsNullOrWhiteSpace(task.Details))
        {
            parts.Add(task.Details);
        }

        parts.Add($"风险：{task.Risk}");
        if (task.Budget.MaxToolCalls > 0 || task.Budget.TokenBudget > 0)
        {
            parts.Add($"预算：工具 {task.Budget.MaxToolCalls} 次，tokens {task.Budget.TokenBudget}");
        }

        if (task.SuggestedTools.Count > 0)
        {
            parts.Add($"建议工具：{string.Join(", ", task.SuggestedTools)}");
        }

        if (task.SuggestedContext.Count > 0)
        {
            parts.Add($"建议上下文：{string.Join(", ", task.SuggestedContext)}");
        }

        return string.Join("；", parts);
    }
}
