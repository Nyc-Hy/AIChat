namespace AIChat.Domain.Chat;

public sealed class AgentStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public int Number { get; set; }
    public AgentStepType Type { get; set; }
    public AgentStepStatus Status { get; set; } = AgentStepStatus.Running;
    public string Title { get; set; } = "";
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public bool IsError { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
}
