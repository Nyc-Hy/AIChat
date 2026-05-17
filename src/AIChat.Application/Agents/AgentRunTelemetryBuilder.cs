using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public static class AgentRunTelemetryBuilder
{
    public static AgentRunTelemetry Build(AgentRun run)
    {
        var verificationCount = run.Verifications.Count;
        var verificationSuccessCount = run.Verifications.Count(verification => verification.IsSuccess);
        var rawArtifactChars = run.Artifacts.Sum(artifact => artifact.Content?.Length ?? 0);
        var summaryArtifactChars = run.Artifacts.Sum(artifact => artifact.Summary?.Length ?? 0);
        var summarizedArtifactCount = run.Artifacts.Count(artifact =>
            artifact.Metadata.TryGetValue("wasSummarized", out var value) &&
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        var telemetry = new AgentRunTelemetry
        {
            EstimatedPromptTokens = run.ContextEstimatedTokens,
            ContextRefCount = run.ContextRefCount,
            ModelCallCount = run.ModelCallCount,
            ToolCallCount = run.ToolCallCount,
            MutationToolSuccessCount = run.FileChanges
                .Select(change => change.ToolCallId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            VerificationCount = verificationCount,
            VerificationSuccessCount = verificationSuccessCount,
            VerificationFailureCount = verificationCount - verificationSuccessCount,
            ApprovalRequiredCount = run.ToolApprovalRequiredCount,
            ApprovalRejectedCount = run.ToolApprovalRejectedCount,
            ArtifactCount = run.Artifacts.Count,
            SummarizedArtifactCount = summarizedArtifactCount,
            RawArtifactChars = rawArtifactChars,
            SummaryArtifactChars = summaryArtifactChars,
            EstimatedSavedChars = Math.Max(0, rawArtifactChars - summaryArtifactChars),
            ToolCallsPerModelCall = run.ModelCallCount <= 0 ? 0 : Math.Round((double)run.ToolCallCount / run.ModelCallCount, 2),
            VerificationPassRate = verificationCount == 0 ? 0 : Math.Round((double)verificationSuccessCount / verificationCount, 2)
        };
        telemetry.OutcomeReason = BuildOutcomeReason(run, telemetry);
        return telemetry;
    }

    public static AgentRunOutcomeKind ClassifyOutcome(AgentRun run)
    {
        if (run.Status == AgentRunStatus.Cancelled)
        {
            return AgentRunOutcomeKind.Cancelled;
        }

        if (run.ToolBudgetExceeded || run.Status == AgentRunStatus.BudgetExceeded)
        {
            return AgentRunOutcomeKind.BudgetExceeded;
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            return AgentRunOutcomeKind.PermissionBlocked;
        }

        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            return AgentRunOutcomeKind.VerificationFailed;
        }

        if (string.Equals(run.CompletionEvidenceStatus, "risk", StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunOutcomeKind.EvidenceRisk;
        }

        if (run.Status == AgentRunStatus.Failed)
        {
            return AgentRunOutcomeKind.Failed;
        }

        if (run.Status == AgentRunStatus.Completed)
        {
            return run.QualityScore >= 70 ? AgentRunOutcomeKind.Success : AgentRunOutcomeKind.PartialSuccess;
        }

        return AgentRunOutcomeKind.Unknown;
    }

    private static string BuildOutcomeReason(AgentRun run, AgentRunTelemetry telemetry)
    {
        var parts = new List<string>
        {
            $"status={run.Status}",
            $"quality={run.QualityScore}",
            $"tools={telemetry.ToolCallCount}",
            $"modelCalls={telemetry.ModelCallCount}",
            $"verification={telemetry.VerificationSuccessCount}/{telemetry.VerificationCount}",
            $"contextTokens~={telemetry.EstimatedPromptTokens}",
            $"savedChars~={telemetry.EstimatedSavedChars}"
        };

        if (!string.IsNullOrWhiteSpace(run.FinalStatusReason))
        {
            parts.Add($"reason={run.FinalStatusReason}");
        }

        return string.Join("; ", parts);
    }
}
