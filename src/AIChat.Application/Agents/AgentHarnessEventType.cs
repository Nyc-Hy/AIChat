namespace AIChat.Application.Agents;

public enum AgentHarnessEventType
{
    RunStarted,
    PhaseChanged,
    StepAdded,
    RawProviderEvent,
    ContentDelta,
    ToolCall,
    ToolApprovalRequired,
    ToolApprovalRejected,
    ToolResult,
    RunCompleted
}
