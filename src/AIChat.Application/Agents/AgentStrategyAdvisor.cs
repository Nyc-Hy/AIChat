using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public sealed class AgentStrategyAdvisor
{
    public AgentTaskExecutionPolicy Adjust(
        AgentTaskExecutionPolicy policy,
        AgentRunContext context,
        IReadOnlyList<AgentRun> history)
    {
        var recent = history
            .Where(run => string.Equals(run.TaskComplexity, policy.Complexity.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.StartedAt)
            .Take(12)
            .ToList();
        var allRecent = history
            .OrderByDescending(run => run.StartedAt)
            .Take(20)
            .ToList();
        if (recent.Count == 0 && allRecent.Count == 0)
        {
            return policy;
        }

        if (!context.AdaptiveStrategiesEnabled)
        {
            return policy;
        }

        var baseLimit = Math.Max(1, context.MaxToolRounds);
        var budgetExceededCount = recent.Count(run => run.ToolBudgetExceeded || run.Status == AgentRunStatus.BudgetExceeded);
        var lowExplorerYield = policy.AllowExplorer &&
                               policy.Complexity == AgentTaskComplexity.Standard &&
                               recent.Count(run => run.ExplorerUsed) >= 2 &&
                               recent.Where(run => run.ExplorerUsed).All(run => run.SubAgentRuns.Count == 0);

        var adjusted = policy;
        var notes = new List<string>();

        if (context.AdaptiveBudgetAndExplorerEnabled &&
            budgetExceededCount >= 2 &&
            policy.MaxToolRounds < baseLimit)
        {
            var extraBudget = policy.Complexity == AgentTaskComplexity.Simple ? 2 : 6;
            var newBudget = Math.Min(baseLimit, policy.MaxToolRounds + extraBudget);
            if (newBudget > policy.MaxToolRounds)
            {
                adjusted = adjusted with { MaxToolRounds = newBudget };
                notes.Add($"recent budget pressure: {policy.MaxToolRounds}->{newBudget}");
            }
        }

        if (context.AdaptiveBudgetAndExplorerEnabled && lowExplorerYield)
        {
            adjusted = adjusted with { AllowExplorer = false };
            notes.Add("standard explorer disabled after low-yield history");
        }

        ApplyUserPreferenceSignals(context, allRecent, ref adjusted, notes);

        if (notes.Count == 0)
        {
            return policy;
        }

        return adjusted with { StrategyAdjustment = string.Join("; ", notes) };
    }

    private static void ApplyUserPreferenceSignals(
        AgentRunContext context,
        IReadOnlyList<AgentRun> history,
        ref AgentTaskExecutionPolicy policy,
        List<string> notes)
    {
        if (history.Count == 0)
        {
            return;
        }

        var continuedRuns = history.Count(run => !string.IsNullOrWhiteSpace(run.ContinuedFromRunId));
        var retriedRuns = history.Count(run => !string.IsNullOrWhiteSpace(run.RetriedFromRunId));
        var rejectedApprovals = history.Sum(run => run.ToolApprovalRejectedCount);
        var mutationRuns = history.Where(run => run.MutationToolSucceeded || run.FileChanges.Count > 0).ToList();
        var unverifiedMutationRuns = mutationRuns.Count(run => run.Verifications.Count == 0);

        if (context.AdaptiveRecoveryEnabled &&
            continuedRuns >= 2 &&
            continuedRuns > retriedRuns &&
            !policy.PreferContinuationRecovery)
        {
            policy = policy with { PreferContinuationRecovery = true };
            notes.Add("user recovery preference: continue from checkpoint");
        }
        else if (context.AdaptiveRecoveryEnabled &&
                 retriedRuns >= 2 &&
                 retriedRuns > continuedRuns &&
                 !policy.PreferCleanRetryRecovery)
        {
            policy = policy with { PreferCleanRetryRecovery = true };
            notes.Add("user recovery preference: clean retry");
        }

        if (context.AdaptiveRecoveryEnabled &&
            rejectedApprovals >= 2 &&
            !policy.CautiousToolApproval)
        {
            policy = policy with { CautiousToolApproval = true };
            notes.Add("user approval preference: explain high-risk tools first");
        }

        if (context.AdaptiveAutoVerifyEnabled &&
            context.VerificationCommands.Count > 0 &&
            mutationRuns.Count >= 2 &&
            unverifiedMutationRuns >= Math.Max(1, mutationRuns.Count / 2) &&
            !policy.ForceAutoVerifyAfterMutation)
        {
            policy = policy with { ForceAutoVerifyAfterMutation = true };
            notes.Add("user quality preference: force auto-verify after mutation");
        }
    }
}
