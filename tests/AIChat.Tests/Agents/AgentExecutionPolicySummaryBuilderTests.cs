using AIChat.Application.Agents;

namespace AIChat.Tests.Agents;

public sealed class AgentExecutionPolicySummaryBuilderTests
{
    [Fact]
    public void Build_IncludesPreferencesAndStrategy()
    {
        var summary = AgentExecutionPolicySummaryBuilder.Build(new AgentTaskExecutionPolicy(
            AgentTaskComplexity.Complex,
            "Standard",
            8,
            3,
            true,
            true,
            PreferContinuationRecovery: true,
            ForceAutoVerifyAfterMutation: true,
            StrategyAdjustment: "recent failures"));

        Assert.Contains("mode=Standard", summary);
        Assert.Contains("preferences=continue,auto-verify", summary);
        Assert.Contains("strategy=recent failures", summary);
    }
}
