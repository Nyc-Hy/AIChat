namespace AIChat.Application.Agents.Budget;

public sealed class AgentBudgetDecision
{
    public bool ShouldPause { get; init; }
    public bool IsHardLimit { get; init; }
    public AgentBudgetCheckpointType CheckpointType { get; init; }
    public string Reason { get; init; } = "";

    public static AgentBudgetDecision Continue() => new()
    {
        ShouldPause = false,
        CheckpointType = AgentBudgetCheckpointType.None
    };

    public static AgentBudgetDecision Pause(AgentBudgetCheckpointType checkpointType, string reason, bool isHardLimit = false) => new()
    {
        ShouldPause = true,
        IsHardLimit = isHardLimit,
        CheckpointType = checkpointType,
        Reason = reason
    };
}
