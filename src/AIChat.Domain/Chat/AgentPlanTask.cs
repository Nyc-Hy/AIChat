namespace AIChat.Domain.Chat;

public sealed class AgentPlanTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = "executing";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public AgentPlanRisk Risk { get; set; } = AgentPlanRisk.Medium;
    public AgentPlanBudget Budget { get; set; } = new();
    public List<string> SuggestedTools { get; set; } = [];
    public List<string> SuggestedContext { get; set; } = [];
    public int Order { get; set; }
}
