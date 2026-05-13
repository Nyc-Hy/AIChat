using AIChat.Application.Agents;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Agents;

public sealed class AgentStrategyAdvisorTests
{
    [Fact]
    public void Adjust_IncreasesBudgetForRepeatedBudgetExceededRuns()
    {
        var policy = new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Standard,
            "Standard Agent Loop",
            MaxToolRounds: 24,
            SubAgentMaxToolCalls: 3,
            UsePlanner: true,
            AllowExplorer: true);
        var history = new[]
        {
            new AgentRun { TaskComplexity = "Standard", ToolBudgetExceeded = true, Status = AgentRunStatus.BudgetExceeded },
            new AgentRun { TaskComplexity = "Standard", ToolBudgetExceeded = true, Status = AgentRunStatus.BudgetExceeded }
        };

        var adjusted = new AgentStrategyAdvisor().Adjust(
            policy,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 40 },
            history);

        Assert.Equal(30, adjusted.MaxToolRounds);
        Assert.Contains("budget pressure", adjusted.StrategyAdjustment);
    }

    [Fact]
    public void Adjust_DisablesLowYieldStandardExplorer()
    {
        var policy = new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Standard,
            "Standard Agent Loop",
            MaxToolRounds: 24,
            SubAgentMaxToolCalls: 3,
            UsePlanner: true,
            AllowExplorer: true);
        var history = new[]
        {
            new AgentRun { TaskComplexity = "Standard", ExplorerUsed = true },
            new AgentRun { TaskComplexity = "Standard", ExplorerUsed = true }
        };

        var adjusted = new AgentStrategyAdvisor().Adjust(
            policy,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 40 },
            history);

        Assert.False(adjusted.AllowExplorer);
        Assert.Contains("explorer disabled", adjusted.StrategyAdjustment);
    }

    [Fact]
    public void Adjust_LeavesStableFastPathAlone()
    {
        var policy = new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Simple,
            "Fast Path",
            MaxToolRounds: 4,
            SubAgentMaxToolCalls: 0,
            UsePlanner: false,
            AllowExplorer: false);

        var adjusted = new AgentStrategyAdvisor().Adjust(
            policy,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 40 },
            [new AgentRun { TaskComplexity = "Simple", Status = AgentRunStatus.Completed, QualityScore = 95 }]);

        Assert.Equal(policy, adjusted);
    }

    [Fact]
    public void Adjust_AppliesRecoveryPreferenceSignalsAcrossRecentHistory()
    {
        var policy = new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Standard,
            "Standard Agent Loop",
            MaxToolRounds: 24,
            SubAgentMaxToolCalls: 3,
            UsePlanner: true,
            AllowExplorer: true);
        var history = new[]
        {
            new AgentRun { TaskComplexity = "Simple", ContinuedFromRunId = "run-1" },
            new AgentRun { TaskComplexity = "Complex", ContinuedFromRunId = "run-2" }
        };

        var adjusted = new AgentStrategyAdvisor().Adjust(
            policy,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 40 },
            history);

        Assert.True(adjusted.PreferContinuationRecovery);
        Assert.Contains("continue from checkpoint", adjusted.StrategyAdjustment);
    }

    [Fact]
    public void Adjust_ForceAutoVerifyWhenMutationHistoryWasOftenUnverified()
    {
        var policy = new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Standard,
            "Standard Agent Loop",
            MaxToolRounds: 24,
            SubAgentMaxToolCalls: 3,
            UsePlanner: true,
            AllowExplorer: true);
        var history = new[]
        {
            new AgentRun { TaskComplexity = "Standard", MutationToolSucceeded = true },
            new AgentRun { TaskComplexity = "Simple", FileChanges = [new AgentFileChange { Path = "src/App.cs" }] }
        };

        var adjusted = new AgentStrategyAdvisor().Adjust(
            policy,
            new AgentRunContext
            {
                ProjectPath = Environment.CurrentDirectory,
                MaxToolRounds = 40,
                VerificationCommands =
                [
                    new ProjectVerificationCommand { Name = "test", Command = "dotnet test" }
                ]
            },
            history);

        Assert.True(adjusted.ForceAutoVerifyAfterMutation);
        Assert.Contains("force auto-verify", adjusted.StrategyAdjustment);
    }

    [Fact]
    public void Adjust_EnablesCautiousToolApprovalAfterRepeatedRejections()
    {
        var policy = new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Standard,
            "Standard Agent Loop",
            MaxToolRounds: 24,
            SubAgentMaxToolCalls: 3,
            UsePlanner: true,
            AllowExplorer: true);

        var adjusted = new AgentStrategyAdvisor().Adjust(
            policy,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 40 },
            [
                new AgentRun { TaskComplexity = "Standard", ToolApprovalRejectedCount = 1 },
                new AgentRun { TaskComplexity = "Simple", ToolApprovalRejectedCount = 1 }
            ]);

        Assert.True(adjusted.CautiousToolApproval);
        Assert.Contains("explain high-risk tools", adjusted.StrategyAdjustment);
    }
}
