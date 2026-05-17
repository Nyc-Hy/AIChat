using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Benchmark;

public static class AgentBenchmarkReportBuilder
{
    public static AgentBenchmarkResult Evaluate(AgentRun run)
    {
        var task = AgentBenchmarkMatcher.Match(run);
        return new AgentBenchmarkEvaluator().Evaluate(task, run);
    }

    public static string BuildRunSummary(AgentRun run)
    {
        var result = Evaluate(run);
        var status = result.Passed ? "通过" : "未通过";
        return $"Benchmark：{result.Name} · {status} · {result.Summary}";
    }

    public static string BuildHistorySummary(IReadOnlyList<AgentRun> runs)
    {
        if (runs.Count == 0)
        {
            return "Benchmark：暂无运行数据";
        }

        var recent = runs
            .OrderByDescending(run => run.StartedAt)
            .Take(20)
            .Select(Evaluate)
            .ToList();
        var passed = recent.Count(result => result.Passed);
        var avgTools = recent.Select(result => result.ToolCallCount).DefaultIfEmpty(0).Average();
        var avgTokens = recent.Select(result => result.EstimatedPromptTokens).DefaultIfEmpty(0).Average();
        var worst = recent
            .Where(result => !result.Passed)
            .GroupBy(result => result.Name)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        var riskText = worst is null ? "主要风险：暂无" : $"主要风险：{worst.Key} {worst.Count()} 次未通过";

        return $"Benchmark：{passed}/{recent.Count} 通过 · 平均工具 {avgTools:0.0} 次 · 平均 Context {avgTokens:0} tokens · {riskText}";
    }
}
