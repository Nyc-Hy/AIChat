namespace AIChat.Domain.Chat;

public sealed class AgentSubAgentRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ParentRunId { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Task { get; set; } = "";
    public string Status { get; set; } = "Running";
    public string Summary { get; set; } = "";
    public string RecommendedNextStep { get; set; } = "";
    public int MaxToolCalls { get; set; }
    public int ToolCallCount { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<string> Findings { get; set; } = [];
    public List<string> ArtifactRefs { get; set; } = [];
    public List<AgentSubAgentToolCall> ToolCalls { get; set; } = [];
}
