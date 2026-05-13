using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunHistoryInsightBuilderTests
{
    [Fact]
    public void Build_ReturnsEmptyStateForNoRuns()
    {
        var insight = AgentRunHistoryInsightBuilder.Build([]);

        Assert.Contains("暂无足够运行数据", insight);
    }

    [Fact]
    public void Build_ReportsContinuationPreferenceFromRecordedSourceIds()
    {
        var insight = AgentRunHistoryInsightBuilder.Build(
        [
            CompletedRun("run-3", continuedFrom: "run-2"),
            CompletedRun("run-2", continuedFrom: "run-1"),
            CompletedRun("run-1")
        ]);

        Assert.Contains("更常选择继续任务", insight);
        Assert.Contains("checkpoint", insight);
    }

    [Fact]
    public void Build_ReportsRetryPreferenceFromRecordedSourceIds()
    {
        var insight = AgentRunHistoryInsightBuilder.Build(
        [
            CompletedRun("run-3", retriedFrom: "run-2"),
            CompletedRun("run-2", retriedFrom: "run-1"),
            CompletedRun("run-1")
        ]);

        Assert.Contains("更常选择重试任务", insight);
        Assert.Contains("干净上下文", insight);
    }

    [Fact]
    public void Build_ReportsApprovalAndVerificationPreferences()
    {
        var insight = AgentRunHistoryInsightBuilder.Build(
        [
            CompletedRun("run-2", rejectedApprovals: 2, mutationSucceeded: true),
            CompletedRun("run-1", mutationSucceeded: true)
        ]);

        Assert.Contains("高风险工具被拒绝偏多", insight);
        Assert.Contains("写入任务缺少验证偏多", insight);
    }

    [Fact]
    public void Build_ReportsAcceptanceFeedback()
    {
        var insight = AgentRunHistoryInsightBuilder.Build(
        [
            CompletedRun("run-2", acceptanceStatus: AgentRunAcceptanceStatus.NeedsChanges),
            CompletedRun("run-1", acceptanceStatus: AgentRunAcceptanceStatus.Accepted)
        ]);

        Assert.Contains("用户验收：通过 1 次 · 需修改 1 次", insight);
        Assert.Contains("用户验收需修改偏多", insight);
    }

    private static AgentRun CompletedRun(
        string id,
        string continuedFrom = "",
        string retriedFrom = "",
        int rejectedApprovals = 0,
        bool mutationSucceeded = false,
        AgentRunAcceptanceStatus acceptanceStatus = AgentRunAcceptanceStatus.Unreviewed)
    {
        return new AgentRun
        {
            Id = id,
            Status = AgentRunStatus.Completed,
            QualityScore = 90,
            ContinuedFromRunId = continuedFrom,
            RetriedFromRunId = retriedFrom,
            ToolApprovalRejectedCount = rejectedApprovals,
            MutationToolSucceeded = mutationSucceeded,
            AcceptanceStatus = acceptanceStatus,
            StartedAt = DateTimeOffset.Now
        };
    }
}
