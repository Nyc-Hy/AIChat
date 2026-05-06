namespace AIChat.Domain.Chat;

public sealed class AgentPlanBudget
{
    public int MaxToolCalls { get; set; }
    public int TokenBudget { get; set; }
    public string Notes { get; set; } = "";
}
