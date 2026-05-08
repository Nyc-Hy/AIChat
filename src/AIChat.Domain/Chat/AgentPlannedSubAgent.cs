namespace AIChat.Domain.Chat;

public sealed class AgentPlannedSubAgent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TemplateId { get; set; } = "explorer";
    public string Phase { get; set; } = "gathering_context";
    public string Task { get; set; } = "";
    public string Reason { get; set; } = "";
    public int MaxToolCalls { get; set; } = 4;
    public int Order { get; set; }
    public List<string> DependsOn { get; set; } = [];
    public List<string> WriteScope { get; set; } = [];
}
