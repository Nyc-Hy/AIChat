using AIChat.Application.Agents;
using AIChat.Application.Context;

namespace AIChat.Tests.Agents;

public sealed class AgentTaskExecutionPolicyBuilderTests
{
    [Fact]
    public void Build_CapsSimpleTasksToSmallBudgetAndNoExplorer()
    {
        var policy = new AgentTaskExecutionPolicyBuilder().Build(
            AgentTaskComplexity.Simple,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 50 },
            new TaskContextPack(),
            isContinuation: false);

        Assert.Equal(12, policy.MaxToolRounds);
        Assert.False(policy.UsePlanner);
        Assert.False(policy.AllowExplorer);
    }

    [Fact]
    public void Build_AllowsStandardExplorerOnlyWhenContextIsMissing()
    {
        var builder = new AgentTaskExecutionPolicyBuilder();

        var withContext = builder.Build(
            AgentTaskComplexity.Standard,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 50 },
            new TaskContextPack { IncludedFiles = [new TaskContextFileRef { Path = "src/App.cs" }] },
            isContinuation: false);
        var withoutContext = builder.Build(
            AgentTaskComplexity.Standard,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 50 },
            new TaskContextPack(),
            isContinuation: false);

        Assert.False(withContext.AllowExplorer);
        Assert.True(withoutContext.AllowExplorer);
    }

    [Fact]
    public void Build_KeepsComplexTasksAtConfiguredBudget()
    {
        var policy = new AgentTaskExecutionPolicyBuilder().Build(
            AgentTaskComplexity.Complex,
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory, MaxToolRounds = 50 },
            new TaskContextPack(),
            isContinuation: false);

        Assert.Equal(50, policy.MaxToolRounds);
        Assert.True(policy.UsePlanner);
        Assert.True(policy.AllowExplorer);
    }
}
