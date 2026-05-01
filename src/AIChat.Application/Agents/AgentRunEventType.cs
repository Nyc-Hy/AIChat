namespace AIChat.Application.Agents;

public enum AgentRunEventType
{
    ContentDelta,
    RawProviderEvent,
    ToolCall,
    ToolApprovalRequired,
    ToolApprovalRejected,
    ToolSessionAllowed,
    ToolResult,
    BudgetExceeded,
    Error,
    Completed
}
