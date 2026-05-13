namespace AIChat.Application.Agents;

public sealed record AgentSmokeTestItem(
    string Title,
    string Detail,
    AgentSmokeTestStatus Status);

public enum AgentSmokeTestStatus
{
    Passed,
    NeedsReview,
    Blocked
}
