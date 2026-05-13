using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public static class AgentRunHistoryInsights
{
    public static string Build(IReadOnlyList<AgentRunHistoryItemViewModel> items)
    {
        if (items.Count == 0)
        {
            return "暂无足够运行数据生成策略建议。";
        }

        var recent = items
            .OrderByDescending(item => item.Run.Run.StartedAt)
            .Take(20)
            .Select(item => item.Run.Run)
            .ToList();
        var completed = recent.Count(run => run.Status == AgentRunStatus.Completed);
        var budgetExceeded = recent.Count(run => run.ToolBudgetExceeded);
        var failedVerification = recent.Count(run => run.Verifications.Any(verification => !verification.IsSuccess));
        var consistencyRisk = recent.Count(run =>
            run.FinalValidationSummary.Contains("一致性风险", StringComparison.OrdinalIgnoreCase) ||
            run.FinalValidationSummary.Contains("存在风险", StringComparison.OrdinalIgnoreCase));
        var averageScore = recent.Where(run => run.QualityScore > 0).Select(run => run.QualityScore).DefaultIfEmpty(0).Average();
        var fastPathRuns = recent.Where(run => run.ExecutionPolicySummary.Contains("mode=Fast Path", StringComparison.OrdinalIgnoreCase)).ToList();
        var fastPathSuccess = fastPathRuns.Count == 0
            ? 0
            : fastPathRuns.Count(run => run.Status == AgentRunStatus.Completed && !run.ToolBudgetExceeded && run.QualityScore >= 85);

        var suggestions = new List<string>
        {
            $"最近 {recent.Count} 次 · 完成 {completed} 次 · 平均评分 {averageScore:0}"
        };

        if (fastPathRuns.Count >= 3 && fastPathSuccess == fastPathRuns.Count)
        {
            suggestions.Add("Fast Path 表现稳定，简单任务可继续保持轻量策略。");
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
}
