namespace AIChat.Application.Agents.Coordinator;

public enum AgentRunPhase
{
    Planning,
    GatheringContext,
    Executing,
    Verifying,
    Repairing,
    Summarizing,
    WaitingForUser,
    Completed,
    Failed,
    Cancelled
}
