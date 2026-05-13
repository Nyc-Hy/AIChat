using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunQualityEvaluatorTests
{
    [Fact]
    public void Evaluate_RewardsStableFastPath()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            ExecutionPolicySummary = "mode=Fast Path; complexity=Simple",
            ModelCallCount = 1,
            MaxToolRounds = 4,
            ToolCallCount = 1,
            FinalValidationSummary = "结果一致性：未检测到需校验的修改或验证声明"
        };

        var result = new AgentRunQualityEvaluator().Evaluate(run);

        Assert.True(result.Score >= 90);
        Assert.Contains("Fast Path 表现稳定", result.StrategySuggestion);
    }

    [Fact]
    public void Evaluate_PenalizesBudgetAndVerificationFailures()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.BudgetExceeded,
            ToolBudgetExceeded = true,
            Verifications =
            [
                new AgentVerification { IsSuccess = false }
            ],
            FinalValidationSummary = "结果一致性：存在风险"
        };

        var result = new AgentRunQualityEvaluator().Evaluate(run);

        Assert.True(result.Score < 60);
        Assert.Contains("预算", result.StrategySuggestion);
        Assert.Contains("验证失败", result.Summary);
        Assert.Contains("一致性风险", result.Summary);
    }

    [Fact]
    public void Evaluate_PenalizesUserRequestedChanges()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            AcceptanceStatus = AgentRunAcceptanceStatus.NeedsChanges,
            AcceptanceNote = "缺少 smoke test"
        };

        var result = new AgentRunQualityEvaluator().Evaluate(run);

        Assert.True(result.Score < 85);
        Assert.Contains("用户验收要求修改", result.Summary);
        Assert.Contains("验收", result.StrategySuggestion);
    }
}
