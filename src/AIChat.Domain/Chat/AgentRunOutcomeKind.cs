namespace AIChat.Domain.Chat;

public enum AgentRunOutcomeKind
{
    Unknown,
    Success,
    PartialSuccess,
    Failed,
    Cancelled,
    VerificationFailed,
    PermissionBlocked,
    BudgetExceeded,
    EvidenceRisk
}
