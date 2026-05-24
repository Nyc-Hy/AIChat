using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Agents;

public sealed record AgentExecutionModeSettings(
    AgentExecutionMode Mode,
    int MaxToolRounds,
    bool AutoVerify,
    int MaxAutoFixRounds,
    bool AdaptiveStrategies,
    bool EnablePlanner,
    bool EnableSubAgents,
    int ContextTokenBudget,
    string Summary);

public static class AgentExecutionModePolicy
{
    public static AgentExecutionMode Parse(string value)
    {
        return Enum.TryParse<AgentExecutionMode>(value, ignoreCase: true, out var mode)
            ? mode
            : AgentExecutionMode.Standard;
    }

    public static AgentExecutionModeSettings Resolve(AgentExecutionMode mode)
    {
        return mode switch
        {
            AgentExecutionMode.Fast => new AgentExecutionModeSettings(
                mode,
                MaxToolRounds: 6,
                AutoVerify: false,
                MaxAutoFixRounds: 0,
                AdaptiveStrategies: false,
                EnablePlanner: false,
                EnableSubAgents: false,
                ContextTokenBudget: 350,
                Summary: "Fast: small read-only or focused edits with minimal context and no automatic repair."),
            AgentExecutionMode.Deep => new AgentExecutionModeSettings(
                mode,
                MaxToolRounds: 40,
                AutoVerify: true,
                MaxAutoFixRounds: 2,
                AdaptiveStrategies: false,
                EnablePlanner: true,
                EnableSubAgents: false,
                ContextTokenBudget: 1600,
                Summary: "Deep: complex code tasks with planner and verification enabled."),
            _ => new AgentExecutionModeSettings(
                AgentExecutionMode.Standard,
                MaxToolRounds: 16,
                AutoVerify: false,
                MaxAutoFixRounds: 0,
                AdaptiveStrategies: false,
                EnablePlanner: false,
                EnableSubAgents: false,
                ContextTokenBudget: 900,
                Summary: "Standard: default single-agent coding loop with bounded context.")
        };
    }

    public static void Apply(AppSettings settings, AgentExecutionMode mode)
    {
        var resolved = Resolve(mode);
        settings.AgentExecutionMode = resolved.Mode;
        settings.AgentMaxToolRounds = resolved.MaxToolRounds;
        settings.AutoVerifyAgentRuns = resolved.AutoVerify;
        settings.MaxAutoFixRounds = resolved.MaxAutoFixRounds;
        settings.AgentAdaptiveStrategiesEnabled = resolved.AdaptiveStrategies;
        settings.AgentAdaptiveBudgetAndExplorerEnabled = resolved.AdaptiveStrategies;
        settings.AgentAdaptiveRecoveryEnabled = resolved.AdaptiveStrategies;
        settings.AgentAdaptiveAutoVerifyEnabled = resolved.AdaptiveStrategies;
    }
}
