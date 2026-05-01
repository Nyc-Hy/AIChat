namespace AIChat.Domain.Audit;

public sealed class AuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string StepId { get; set; } = "";
    public AuditEventType Type { get; set; }
    public string ToolName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}

public enum AuditEventType
{
    ToolCallRequested,
    ToolCallApproved,
    ToolCallRejected,
    ToolCallSessionAllowed,
    FileWritten,
    ShellExecuted,
    RollbackPerformed,
    VerificationRun,
    AgentRunStarted,
    AgentRunCompleted,
    AgentRunFailed,
    AgentRunCancelled
}
