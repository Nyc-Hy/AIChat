namespace AIChat.Application.Agents;

public enum AgentRunEventType
{
    ModelRequestStarted,
    ContentDelta,
    RawProviderEvent,
    // 2026-08-05: emitted once per model call when
    // the platform returns a usage block on the
    // streaming response. Carries the prompt /
    // completion / cached breakdown so the runner
    // can surface the cache hit rate in the activity
    // feed and status bar.
    RunUsage,
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
