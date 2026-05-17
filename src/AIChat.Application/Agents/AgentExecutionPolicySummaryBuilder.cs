namespace AIChat.Application.Agents;

public static class AgentExecutionPolicySummaryBuilder
{
    public static string Build(AgentTaskExecutionPolicy policy)
    {
        var summary = $"mode={policy.Mode}; complexity={policy.Complexity}; maxToolRounds={policy.MaxToolRounds}; planner={policy.UsePlanner}; explorer={policy.AllowExplorer}; subAgentMaxToolCalls={policy.SubAgentMaxToolCalls}";
        var preferences = FormatPreferences(policy);
        if (!string.IsNullOrWhiteSpace(preferences))
        {
            summary += $"; preferences={preferences}";
        }

        return string.IsNullOrWhiteSpace(policy.StrategyAdjustment)
            ? summary
            : summary + $"; strategy={policy.StrategyAdjustment}";
    }

    public static string FormatPreferences(AgentTaskExecutionPolicy policy)
    {
        var preferences = new List<string>();
        if (policy.PreferContinuationRecovery)
        {
            preferences.Add("continue");
        }

        if (policy.PreferCleanRetryRecovery)
        {
            preferences.Add("clean-retry");
        }

        if (policy.ForceAutoVerifyAfterMutation)
        {
            preferences.Add("auto-verify");
        }

        if (policy.CautiousToolApproval)
        {
            preferences.Add("cautious-approval");
        }

        return string.Join(",", preferences);
    }
}
