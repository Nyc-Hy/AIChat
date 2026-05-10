namespace AIChat.Domain.Chat;

public enum AgentRunStatus
{
    Running,
    Completed,
    BudgetExceeded,
    Cancelled,
    Failed
}
