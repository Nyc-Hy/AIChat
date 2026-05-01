namespace AIChat.Domain.Chat;

public sealed class AgentVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public string StepId { get; set; } = "";
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Command { get; set; } = "";
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool IsSuccess { get; set; }
    public string Output { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
