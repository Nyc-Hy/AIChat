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
        if (recent.Count == 0)
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

        if (lowExplorerYield)
        {
            adjusted = adjusted with { AllowExplorer = false };
            notes.Add("standard explorer disabled after low-yield history");
        }

        if (notes.Count == 0)
        {
            return policy;
        }

        return adjusted with { StrategyAdjustment = string.Join("; ", notes) };
    }
}
