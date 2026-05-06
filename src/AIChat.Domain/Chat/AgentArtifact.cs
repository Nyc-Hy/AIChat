namespace AIChat.Domain.Chat;

public sealed class AgentArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public string StepId { get; set; } = "";
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Kind { get; set; } = "tool_result";
    public string Summary { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
