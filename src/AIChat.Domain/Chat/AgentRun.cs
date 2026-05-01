namespace AIChat.Domain.Chat;

// One assistant turn may involve several model/tool steps. AgentRun keeps that
// sequence explicit so the UI can show more than a pile of tool cards.
public sealed class AgentRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string UserMessageId { get; set; } = "";
    public string AssistantMessageId { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Phase { get; set; } = "planning";
    public string CompletionReason { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Model { get; set; } = "";
    public List<string> EnabledTools { get; set; } = [];
    public Dictionary<string, string> ToolPermissionModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string WorkspaceBranch { get; set; } = "";
    public int WorkspaceChangeCountAtStart { get; set; }
    public bool WorkspaceChangesWereTruncated { get; set; }
    public int MaxToolRounds { get; set; }
    public int ToolCallCount { get; set; }
    public bool ToolBudgetExceeded { get; set; }
    public bool RequiresProjectMutation { get; set; }
    public bool MutationToolSucceeded { get; set; }
    public int ToolApprovalRequiredCount { get; set; }
    public int ToolApprovalRejectedCount { get; set; }
    public int ToolSessionAllowedCount { get; set; }
    public string FinalValidationSummary { get; set; } = "";
    public string RecoverySuggestion { get; set; } = "";
    public string ContinuedFromRunId { get; set; } = "";
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Running;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public AgentPlan? Plan { get; set; }
    public List<AgentStep> Steps { get; set; } = [];
    public List<AgentFileChange> FileChanges { get; set; } = [];
    public List<AgentVerification> Verifications { get; set; } = [];

    public void Complete(AgentRunStatus status, DateTimeOffset? completedAt = null, string completionReason = "")
    {
        Status = status;
        Phase = status switch
        {
            AgentRunStatus.Completed => "completed",
            AgentRunStatus.Cancelled => "cancelled",
            AgentRunStatus.Failed => "failed",
            _ => Phase
        };
        if (!string.IsNullOrWhiteSpace(completionReason))
        {
            CompletionReason = completionReason;
        }
        CompletedAt = completedAt ?? DateTimeOffset.Now;
    }
}
