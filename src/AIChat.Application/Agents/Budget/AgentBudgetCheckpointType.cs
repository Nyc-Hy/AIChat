namespace AIChat.Application.Agents.Budget;

public enum AgentBudgetCheckpointType
{
    None,
    ToolInterval,
    HighRiskMutation,
    BudgetSegment,
    VerificationFailureLoop,
    HardLimit
}
