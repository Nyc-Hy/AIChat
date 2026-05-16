namespace AIChat.Application.Agents;

public enum AgentRunEventType
{
    ModelRequestStarted,
    ContentDelta,
    RawProviderEvent,
    ToolCall,
    ToolApprovalRequired,
    ToolApprovalRejected,
    ToolSessionAllowed,
    ToolResult,
    BudgetExceeded,
    Cancelled,
    Error,
    Completed
}
