using AIChat.Application.Agents.SubAgents;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Run-policy / decision helpers — split out from the main
// AgentHarness partial so the orchestration file stays focused
// on the run loop. DetermineFinalStatus classifies the
// terminal run state from the run's accumulated facts (tool
// budget, verifications), CreateExplorerDecisionReason
// produces the human-readable reason why a given sub-agent
// schedule did or did not run, ApplyHistoricalAdjustments is
// the inline-of-deleted-AgentStrategyAdvisor bump that
// widens MaxToolRounds when the user keeps hitting the
// budget, and ApplyExecutionPolicy derives the per-run
// AgentRunContext from the policy + ambient context.
public sealed partial class AgentHarness
{
    private static AgentRunStatus DetermineFinalStatus(AgentRun run)
    {
        if (run.ToolBudgetExceeded)
        {
            return AgentRunStatus.BudgetExceeded;
        }

        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            return AgentRunStatus.Failed;
        }

        return AgentRunStatus.Completed;
    }

    private static string CreateExplorerDecisionReason(
        AgentTaskExecutionPolicy policy,
        IReadOnlyList<AgentSubAgentScheduleDecision> schedule,
        IReadOnlyList<AgentSubAgentScheduleDecision> scheduled)
    {
        if (!policy.AllowExplorer)
        {
            return "Explorer skipped by execution policy.";
        }

        if (scheduled.Count > 0)
        {
            return $"Explorer scheduled: {scheduled.Count}.";
        }

        if (schedule.Count > 0)
        {
            var skipped = schedule.FirstOrDefault(decision => !string.IsNullOrWhiteSpace(decision.SkipReason));
            return string.IsNullOrWhiteSpace(skipped?.SkipReason)
                ? "Explorer not scheduled after coordinator filtering."
                : "Explorer skipped: " + skipped.SkipReason;
        }

        return "Explorer allowed but no schedule was produced.";
    }

    // Inline of the previous AgentStrategyAdvisor.Adjust: the
    // "should I bump MaxToolRounds because the user keeps hitting
    // the tool budget" decision lives here so the only collaborator
    // of AgentHarness stays AgentTaskExecutionPolicyBuilder. The
    // logic is the same as the deleted AgentStrategyAdvisor —
    // when adaptive flags are off, the policy is returned as-is.
    private static AgentTaskExecutionPolicy ApplyHistoricalAdjustments(
        AgentTaskExecutionPolicy policy,
        AgentRunContext context,
        IReadOnlyList<AgentRun> history)
    {
        if (history.Count == 0)
        {
            return policy;
        }

        var notes = new List<string>();
        var adjusted = policy;

        if (context.AdaptiveStrategiesEnabled && context.AdaptiveBudgetAndExplorerEnabled)
        {
            var baseLimit = Math.Max(1, context.MaxToolRounds);
            var recentSameComplexity = history
                .Where(run => string.Equals(run.TaskComplexity, policy.Complexity.ToString(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(run => run.StartedAt)
                .Take(12)
                .ToList();
            var budgetExceededCount = recentSameComplexity
                .Count(run => run.ToolBudgetExceeded || run.Status == AgentRunStatus.BudgetExceeded);

            if (budgetExceededCount >= 2 && policy.MaxToolRounds < baseLimit)
            {
                var extraBudget = policy.Complexity == AgentTaskComplexity.Simple ? 2 : 6;
                var newBudget = Math.Min(baseLimit, policy.MaxToolRounds + extraBudget);
                if (newBudget > policy.MaxToolRounds)
                {
                    adjusted = adjusted with { MaxToolRounds = newBudget };
                    notes.Add($"recent budget pressure: {policy.MaxToolRounds}->{newBudget}");
                }
            }
        }

        return notes.Count == 0 ? policy : adjusted with { StrategyAdjustment = string.Join("; ", notes) };
    }

    private static AgentRunContext ApplyExecutionPolicy(
        AgentRunContext context,
        AgentTaskExecutionPolicy policy)
    {
        var autoVerify = context.AutoVerifyAgentRuns || policy.ForceAutoVerifyAfterMutation;
        if (context.MaxToolRounds == policy.MaxToolRounds &&
            context.AutoVerifyAgentRuns == autoVerify)
        {
            return context;
        }

        return new AgentRunContext
        {
            ProjectPath = context.ProjectPath,
            EnabledToolIds = context.EnabledToolIds,
            ToolPermissionModes = context.ToolPermissionModes,
            RequestToolApprovalAsync = context.RequestToolApprovalAsync,
            MaxToolRounds = policy.MaxToolRounds,
            ProjectPreparationSucceeded = context.ProjectPreparationSucceeded,
            ProjectPreparationSummary = context.ProjectPreparationSummary,
            ProjectAgentsAvailable = context.ProjectAgentsAvailable,
            ProjectVerificationCommandCount = context.ProjectVerificationCommandCount,
            AutoVerifyAgentRuns = autoVerify,
            MaxAutoFixRounds = context.MaxAutoFixRounds,
            AdaptiveStrategiesEnabled = context.AdaptiveStrategiesEnabled,
            AdaptiveBudgetAndExplorerEnabled = context.AdaptiveBudgetAndExplorerEnabled,
            VerificationCommands = context.VerificationCommands,
            InputArtifacts = context.InputArtifacts
        };
    }
}
