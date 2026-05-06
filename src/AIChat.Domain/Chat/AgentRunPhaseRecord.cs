namespace AIChat.Domain.Chat;

public sealed class AgentRunPhaseRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Status { get; set; } = "running";
    public string Summary { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
}
