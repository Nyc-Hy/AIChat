namespace AIChat.Domain.Chat;

public sealed class AgentRunTelemetry
{
    public int EstimatedPromptTokens { get; set; }
    public int ContextRefCount { get; set; }
    public int ModelCallCount { get; set; }
    public int ToolCallCount { get; set; }
    public int MutationToolSuccessCount { get; set; }
    public int VerificationCount { get; set; }
    public int VerificationSuccessCount { get; set; }
    public int VerificationFailureCount { get; set; }
    public int ApprovalRequiredCount { get; set; }
    public int ApprovalRejectedCount { get; set; }
    public int ArtifactCount { get; set; }
    public int SummarizedArtifactCount { get; set; }
    public int RawArtifactChars { get; set; }
    public int SummaryArtifactChars { get; set; }
    public int EstimatedSavedChars { get; set; }
    public double ToolCallsPerModelCall { get; set; }
    public double VerificationPassRate { get; set; }
    public string OutcomeReason { get; set; } = "";
}
