using AIChat.Application.Agents.Benchmark;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents.Benchmark;

public sealed class AgentBenchmarkEvaluatorTests
{
    [Fact]
    public void Evaluate_PassesWhenRunMeetsTaskCriteria()
    {
        var task = new AgentBenchmarkTask(
            "bugfix",
            "Bugfix",
            "bugfix",
            "fix bug",
            RequiresMutation: true,
            RequiresVerification: true,
            MaxToolCalls: 6,
            MaxEstimatedPromptTokens: 5000);
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            QualityScore = 90,
            MutationToolSucceeded = true,
            ToolCallCount = 4,
            ContextEstimatedTokens = 3000,
            CompletionEvidenceStatus = "satisfied",
            Verifications = { new AgentVerification { IsSuccess = true } }
        };

        var result = new AgentBenchmarkEvaluator().Evaluate(task, run);

        Assert.True(result.Passed);
        Assert.Equal(AgentRunOutcomeKind.Success, result.Outcome);
    }

    [Fact]
    public void Evaluate_FailsWhenVerificationMissingOrBudgetsExceeded()
    {
        var task = new AgentBenchmarkTask(
            "bugfix",
            "Bugfix",
            "bugfix",
            "fix bug",
            RequiresMutation: true,
            RequiresVerification: true,
            MaxToolCalls: 2,
            MaxEstimatedPromptTokens: 1000);
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            QualityScore = 90,
            MutationToolSucceeded = true,
            ToolCallCount = 4,
            ContextEstimatedTokens = 3000,
            CompletionEvidenceStatus = "satisfied"
        };

        var result = new AgentBenchmarkEvaluator().Evaluate(task, run);

        Assert.False(result.Passed);
        Assert.Contains("verification-missing", result.Summary);
        Assert.Contains("tool-budget", result.Summary);
        Assert.Contains("prompt-budget", result.Summary);
    }
}
