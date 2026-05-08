namespace AIChat.Application.Agents;

public enum AgentHarnessEventType
{
    RunStarted,
    PhaseChanged,
    StepAdded,
    RawProviderEvent,
    SubAgentStarted,
    SubAgentCompleted,
    ContentDelta,
    ToolCall,
    ToolApprovalRequired,
    ToolApprovalRejected,
    ToolResult,
    RunCompleted
}
