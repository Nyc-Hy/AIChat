namespace AIChat.Application.Agents;

public enum AgentHarnessEventType
{
    RunStarted,
    StepAdded,
    RawProviderEvent,
    ContentDelta,
    ToolCall,
    ToolApprovalRequired,
    ToolApprovalRejected,
    ToolResult,
    RunCompleted
}
