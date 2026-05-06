namespace AIChat.Domain.Chat;

public sealed class AgentPlanPhase
{
    public string Name { get; set; } = "executing";
    public string Objective { get; set; } = "";
    public List<AgentPlanTask> Tasks { get; set; } = [];
}
