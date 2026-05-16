using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunDiagnosticSummaryBuilderTests
{
    [Fact]
    public void Build_ExplainsBudgetPausedRun()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.BudgetExceeded,
            ToolBudgetExceeded = true
        };

        var summary = AgentRunDiagnosticSummaryBuilder.Build(run);

        Assert.Contains("预算", summary.BlockingReason);
        Assert.Contains("恢复提示", summary.NextAction);
        Assert.Contains("预算耗尽", summary.AttentionSummary);
    }

    [Fact]
    public void Build_PrioritizesFailedVerification()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Failed,
            Verifications =
            [
                new AgentVerification
                {
                    ToolName = "run_test",
                    Command = "dotnet test",
                    ExitCode = 1,
                    IsSuccess = false
                }
            ]
        };

        var summary = AgentRunDiagnosticSummaryBuilder.Build(run);

        Assert.Contains("dotnet test", summary.BlockingReason);
        Assert.Contains("重跑失败命令", summary.NextAction);
        Assert.Contains("验证失败 1", summary.AttentionSummary);
    }

    [Fact]
    public void Build_SuggestsVerificationWhenMutationCompletedWithoutVerification()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            MutationToolSucceeded = true
        };

        var summary = AgentRunDiagnosticSummaryBuilder.Build(run);

        Assert.Equal("没有阻塞。", summary.BlockingReason);
        Assert.Contains("补跑项目验证", summary.NextAction);
        Assert.Equal("暂无需特别处理的风险。", summary.AttentionSummary);
    }
}
