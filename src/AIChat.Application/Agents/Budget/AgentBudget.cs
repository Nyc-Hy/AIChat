namespace AIChat.Application.Agents.Budget;

public sealed class AgentBudget
{
    public int MaxToolCalls { get; init; } = 50;
    public int MaxModelTokens { get; init; } = 0;
    public TimeSpan? MaxElapsedTime { get; init; }
    public int ToolCheckpointInterval { get; init; } = 10;
    public int PhaseToolCallLimit { get; init; } = 0;
    public int SubAgentToolCallLimit { get; init; } = 4;
    public bool PauseBeforeHighRiskMutation { get; init; } = true;
    public bool PauseAfterVerificationFailureLoop { get; init; } = true;
}
