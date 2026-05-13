using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public static class AgentRunPerformanceSummaryBuilder
{
    public static string Build(IReadOnlyList<AgentRun> runs)
    {
        if (runs.Count == 0)
        {
            return "性能：暂无运行数据";
        }

        var recent = runs
            .OrderByDescending(run => run.StartedAt)
            .Take(20)
            .ToList();
        var completed = recent.Count(run => run.Status == AgentRunStatus.Completed);
        var avgDuration = recent
            .Select(GetDurationSeconds)
            .Where(value => value > 0)
            .DefaultIfEmpty(0)
            .Average();
        var avgModelCalls = recent.Select(run => run.ModelCallCount).DefaultIfEmpty(0).Average();
        var avgToolCalls = recent.Select(run => run.ToolCallCount).DefaultIfEmpty(0).Average();
        var avgContext = recent.Select(run => run.ContextEstimatedTokens).DefaultIfEmpty(0).Average();
        var fastPathCount = recent.Count(IsFastPathRun);
        var accepted = recent.Count(run => run.AcceptanceStatus == AgentRunAcceptanceStatus.Accepted);
        var needsChanges = recent.Count(run => run.AcceptanceStatus == AgentRunAcceptanceStatus.NeedsChanges);
        var reviewed = accepted + needsChanges;
        var avgQuality = recent
            .Where(run => run.QualityScore > 0)
            .Select(run => run.QualityScore)
            .DefaultIfEmpty(0)
            .Average();

        var completionRate = Percent(completed, recent.Count);
        var fastPathRate = Percent(fastPathCount, recent.Count);
        var acceptanceRate = reviewed == 0 ? "未验收" : Percent(accepted, reviewed);

        return $"性能：完成率 {completionRate} · 平均耗时 {FormatSeconds(avgDuration)} · 模型 {avgModelCalls:0.0} 次 · 工具 {avgToolCalls:0.0} 次 · Context {avgContext:0} tokens · Fast Path {fastPathRate} · 验收通过 {acceptanceRate} · 质量 {avgQuality:0}/100";
    }

    private static double GetDurationSeconds(AgentRun run)
    {
        var completedAt = run.CompletedAt;
        if (completedAt is null)
        {
            return 0;
        }

        return Math.Max(0, (completedAt.Value - run.StartedAt).TotalSeconds);
    }

    private static bool IsFastPathRun(AgentRun run)
    {
        return run.ExecutionPolicySummary.Contains("mode=Fast Path", StringComparison.OrdinalIgnoreCase);
    }

    private static string Percent(int value, int total)
    {
        if (total <= 0)
        {
            return "0%";
        }

        return $"{(double)value / total:P0}";
    }

    private static string FormatSeconds(double seconds)
    {
        if (seconds <= 0)
        {
            return "未记录";
        }

        return seconds < 60
            ? $"{seconds:0.0}s"
            : $"{TimeSpan.FromSeconds(seconds).TotalMinutes:0.0}m";
    }
}
