namespace AIChat.Domain.Chat;

public sealed class AgentFileChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public string StepId { get; set; } = "";
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Path { get; set; } = "";
    public string DiffText { get; set; } = "";
    public int OldChars { get; set; }
    public int NewChars { get; set; }
    public string ContentSnapshot { get; set; } = "";
    public string PostChangeHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
