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
            AgentAdaptiveStrategiesEnabled = false,
            AgentAdaptiveBudgetAndExplorerEnabled = false,
            AgentAdaptiveRecoveryEnabled = false,
            AgentAdaptiveAutoVerifyEnabled = false
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal(7, roundTripped.AgentMaxToolRounds);
        Assert.False(roundTripped.AgentAdaptiveStrategiesEnabled);
        Assert.False(roundTripped.AgentAdaptiveBudgetAndExplorerEnabled);
        Assert.False(roundTripped.AgentAdaptiveRecoveryEnabled);
        Assert.False(roundTripped.AgentAdaptiveAutoVerifyEnabled);
    }
}
