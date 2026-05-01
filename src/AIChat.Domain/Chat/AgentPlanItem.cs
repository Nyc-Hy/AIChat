namespace AIChat.Domain.Chat;

public sealed class AgentPlanItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public AgentPlanItemStatus Status { get; set; } = AgentPlanItemStatus.Pending;
    public string Notes { get; set; } = "";
    public int Order { get; set; }
}
