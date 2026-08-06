using System.Text.Json;
using AIChat.Abstractions.Configuration;

namespace AIChat.Tests.Configuration;

public sealed class AppSettingsSerializationTests
{
    [Fact]
    public void AppSettings_RoundTripsAgentHarnessBudget()
    {
        var settings = new AppSettings
        {
            AgentMaxToolRounds = 7,
            AgentExecutionMode = AgentExecutionMode.Deep,
            AgentAdaptiveStrategiesEnabled = false,
            AgentAdaptiveBudgetAndExplorerEnabled = false
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal(7, roundTripped.AgentMaxToolRounds);
        Assert.Equal(AgentExecutionMode.Deep, roundTripped.AgentExecutionMode);
        Assert.False(roundTripped.AgentAdaptiveStrategiesEnabled);
        Assert.False(roundTripped.AgentAdaptiveBudgetAndExplorerEnabled);
    }
}
