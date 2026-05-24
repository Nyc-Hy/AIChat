using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;

namespace AIChat.Tests.Agents;

public sealed class AgentExecutionModePolicyTests
{
    [Theory]
    [InlineData("fast", AgentExecutionMode.Fast, 6, false, 0)]
    [InlineData("standard", AgentExecutionMode.Standard, 16, false, 0)]
    [InlineData("deep", AgentExecutionMode.Deep, 40, true, 2)]
    public void Resolve_ReturnsExpectedPreset(
        string value,
        AgentExecutionMode expectedMode,
        int expectedToolRounds,
        bool expectedPlanner,
        int expectedAutoFixRounds)
    {
        var mode = AgentExecutionModePolicy.Parse(value);
        var settings = AgentExecutionModePolicy.Resolve(mode);

        Assert.Equal(expectedMode, settings.Mode);
        Assert.Equal(expectedToolRounds, settings.MaxToolRounds);
        Assert.Equal(expectedPlanner, settings.EnablePlanner);
        Assert.Equal(expectedAutoFixRounds, settings.MaxAutoFixRounds);
    }

    [Fact]
    public void Apply_UpdatesRuntimeSettings()
    {
        var settings = new AppSettings();

        AgentExecutionModePolicy.Apply(settings, AgentExecutionMode.Fast);

        Assert.Equal(AgentExecutionMode.Fast, settings.AgentExecutionMode);
        Assert.Equal(6, settings.AgentMaxToolRounds);
        Assert.False(settings.AutoVerifyAgentRuns);
    }
}
