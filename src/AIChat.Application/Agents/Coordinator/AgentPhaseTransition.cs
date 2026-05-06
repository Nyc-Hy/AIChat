namespace AIChat.Application.Agents.Coordinator;

public sealed record AgentPhaseTransition(
    AgentRunPhase Phase,
    string PhaseKey,
    string Status,
    string Summary);
