using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public static class AgentRunHistoryInsightBuilder
{
    public static string Build(IReadOnlyList<AgentRun> runs)
    {
        if (runs.Count == 0)
        {
            return "暂无足够运行数据生成策略建议。";
        }

        var recent = runs
            .OrderByDescending(run => run.StartedAt)
            .Take(20)
            .ToList();
        var completed = recent.Count(run => run.Status == AgentRunStatus.Completed);
        var budgetExceeded = recent.Count(run => run.ToolBudgetExceeded);
        var failedVerification = recent.Count(run => run.Verifications.Any(verification => !verification.IsSuccess));
        var consistencyRisk = recent.Count(HasConsistencyRisk);
        var averageScore = recent.Where(run => run.QualityScore > 0).Select(run => run.QualityScore).DefaultIfEmpty(0).Average();
        var fastPathRuns = recent.Where(IsFastPathRun).ToList();
        var fastPathSuccess = fastPathRuns.Count == 0
            ? 0
            : fastPathRuns.Count(run => run.Status == AgentRunStatus.Completed && !run.ToolBudgetExceeded && run.QualityScore >= 85);
        var continuedRuns = recent.Count(run => !string.IsNullOrWhiteSpace(run.ContinuedFromRunId));
        var retriedRuns = recent.Count(run => !string.IsNullOrWhiteSpace(run.RetriedFromRunId));
        var approvalRejected = recent.Sum(run => run.ToolApprovalRejectedCount);
        var mutationRuns = recent.Where(run => run.MutationToolSucceeded || run.FileChanges.Count > 0).ToList();
        var unverifiedMutationRuns = mutationRuns.Count(run => run.Verifications.Count == 0);

        var suggestions = new List<string>
        {
            $"最近 {recent.Count} 次 · 完成 {completed} 次 · 平均评分 {averageScore:0}"
        };

        if (fastPathRuns.Count >= 3 && fastPathSuccess == fastPathRuns.Count)
        {
            suggestions.Add("Fast Path 表现稳定，简单任务可继续保持轻量策略。");
        }

        if (continuedRuns >= 2 && continuedRuns > retriedRuns)
        {
            suggestions.Add("用户更常选择继续任务，建议优先保留 checkpoint 和自动续跑入口。");
        }
        else if (retriedRuns >= 2 && retriedRuns > continuedRuns)
        {
            suggestions.Add("用户更常选择重试任务，建议让失败恢复从干净上下文开始并保留原始目标。");
        }

        if (approvalRejected >= 2)
        {
            suggestions.Add("高风险工具被拒绝偏多，建议写入前展示更明确的工具目的和影响范围。");
        }

        if (mutationRuns.Count >= 2 && unverifiedMutationRuns >= Math.Max(1, mutationRuns.Count / 2))
        {
            suggestions.Add("写入任务缺少验证偏多，建议默认运行项目验证命令再完成。");
        }

        if (budgetExceeded >= Math.Max(2, recent.Count / 4))
        {
            suggestions.Add("预算耗尽偏多，建议提高同类任务工具上限或更积极使用继续任务。");
        }

        if (failedVerification > 0)
        {
            suggestions.Add("存在验证失败记录，写入任务建议保持自动验证开启。");
        }

        if (consistencyRisk > 0)
        {
            suggestions.Add("存在一致性风险，建议要求最终回复引用真实工具记录。");
        }

        if (suggestions.Count == 1)
        {
            suggestions.Add("当前策略表现平稳，继续观察更多运行数据。");
        }

        return string.Join(Environment.NewLine, suggestions);
    }

    private static bool IsFastPathRun(AgentRun run)
    {
        return run.ExecutionPolicySummary.Contains("mode=Fast Path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasConsistencyRisk(AgentRun run)
    {
        return run.FinalValidationSummary.Contains("一致性风险", StringComparison.OrdinalIgnoreCase) ||
               run.FinalValidationSummary.Contains("存在风险", StringComparison.OrdinalIgnoreCase);
    }
}
