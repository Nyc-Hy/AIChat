using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunPerformanceSummaryBuilderTests
{
    [Fact]
    public void Build_ReturnsEmptyStateForNoRuns()
    {
        var summary = AgentRunPerformanceSummaryBuilder.Build([]);

        Assert.Contains("暂无运行数据", summary);
    }

    [Fact]
    public void Build_SummarizesSpeedCostAndAcceptance()
    {
        var started = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        var summary = AgentRunPerformanceSummaryBuilder.Build(
        [
            new AgentRun
            {
                Status = AgentRunStatus.Completed,
                StartedAt = started,
                CompletedAt = started.AddSeconds(10),
                ModelCallCount = 1,
                ToolCallCount = 2,
                ContextEstimatedTokens = 1000,
                QualityScore = 90,
                ExecutionPolicySummary = "mode=Fast Path",
                AcceptanceStatus = AgentRunAcceptanceStatus.Accepted
            },
            new AgentRun
            {
                Status = AgentRunStatus.Failed,
                StartedAt = started,
                CompletedAt = started.AddSeconds(20),
                ModelCallCount = 3,
                ToolCallCount = 4,
                ContextEstimatedTokens = 3000,
                QualityScore = 70,
                AcceptanceStatus = AgentRunAcceptanceStatus.NeedsChanges
            }
        ]);

        Assert.Contains("完成率 50%", summary);
        Assert.Contains("平均耗时 15.0s", summary);
        Assert.Contains("模型 2.0 次", summary);
        Assert.Contains("工具 3.0 次", summary);
        Assert.Contains("Context 2000 tokens", summary);
        Assert.Contains("Fast Path 50%", summary);
        Assert.Contains("验收通过 50%", summary);
        Assert.Contains("质量 80/100", summary);
    }
}
