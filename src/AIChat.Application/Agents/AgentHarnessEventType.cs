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
    // 2026-08-05: emitted once per model call when
    // the platform returns a usage block on the
    // streaming response. Carries the prompt /
    // completion / cached breakdown so the runner
    // can surface the cache hit rate in the activity
    // feed and status bar. Set on the chunk whose
    // IsCompleted=true OR on a usage-only chunk
    // (some providers emit the usage block in a
    // separate envelope between the last content
    // delta and the [DONE] sentinel).
    RunUsage,
    RunCompleted
}
