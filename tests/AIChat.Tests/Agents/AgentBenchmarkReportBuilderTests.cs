using AIChat.Application.Agents.Benchmark;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentBenchmarkReportBuilderTests
{
    [Fact]
    public void Match_ChoosesPluginBenchmarkForPluginRuns()
    {
        var run = new AgentRun
        {
            Goal = "完善插件系统并调用 MCP 工具",
            Status = AgentRunStatus.Completed,
            QualityScore = 90,
            ToolCallCount = 4,
            ModelCallCount = 2,
            ContextEstimatedTokens = 3000
        };

        var task = AgentBenchmarkMatcher.Match(run);

        Assert.Equal("plugin-mcp", task.Id);
    }

    [Fact]
    public void BuildRunSummary_EvaluatesMatchedTask()
    {
        var run = new AgentRun
        {
            Goal = "分析项目结构",
            Status = AgentRunStatus.Completed,
            QualityScore = 88,
            ToolCallCount = 3,
            ModelCallCount = 1,
            ContextEstimatedTokens = 2500,
            OutcomeKind = AgentRunOutcomeKind.Success
        };

        var summary = AgentBenchmarkReportBuilder.BuildRunSummary(run);

        Assert.Contains("只读项目分析", summary);
        Assert.Contains("通过", summary);
    }

    [Fact]
    public void BuildHistorySummary_ReportsPassRateAndBudgetAverages()
    {
        var started = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero);
        var summary = AgentBenchmarkReportBuilder.BuildHistorySummary(
        [
            new AgentRun
            {
                Goal = "分析项目结构",
                Status = AgentRunStatus.Completed,
                StartedAt = started,
                QualityScore = 90,
                ToolCallCount = 2,
                ContextEstimatedTokens = 1000
            },
            new AgentRun
            {
                Goal = "修复 bug",
                Status = AgentRunStatus.Completed,
                StartedAt = started.AddMinutes(1),
                QualityScore = 50,
                ToolCallCount = 20,
                ContextEstimatedTokens = 9000,
                RequiresProjectMutation = true
            }
        ]);

        Assert.Contains("1/2 通过", summary);
        Assert.Contains("平均工具 11.0 次", summary);
        Assert.Contains("平均 Context 5000 tokens", summary);
        Assert.Contains("主要风险", summary);
    }
}
