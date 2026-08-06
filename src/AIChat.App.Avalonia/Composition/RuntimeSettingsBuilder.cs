using AIChat.Abstractions.Configuration;
using AIChat.Application.Tools;

namespace AIChat.App.Avalonia.Composition;

// Builds the AppSettings instances handed to the agent harness for each
// execution mode. PR-7 scope: extract the three builders from
// MainWindowViewModel so the runtime/policy decisions live in a
// dedicated module that the agent loop can grow without further
// inflating the UI.
public static class RuntimeSettingsBuilder
{
    // Read-only mode: only read-only tools are enabled, with a tighter
    // round budget so a stuck agent doesn't burn the whole budget.
    public static AppSettings ReadOnly(AppSettings source, AgentToolRegistry registry)
    {
        var configured = ResolveConfiguredTools(source, registry);
        var enabledToolIds = registry.All
            .Where(tool => configured.EnabledIds.Contains(tool.Id))
            .Where(tool => tool.Risk == AgentToolRisk.ReadOnly || IsUpdatePlan(tool.Id))
            .Where(tool => configured.Modes[tool.Id] != ToolPermissionMode.Disabled)
            .Select(tool => tool.Id)
            .ToList();
        var enabledSet = enabledToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modes = registry.All.ToDictionary(
            tool => tool.Id,
            tool => enabledSet.Contains(tool.Id)
                ? configured.Modes[tool.Id]
                : ToolPermissionMode.Disabled,
            StringComparer.OrdinalIgnoreCase);

        return new AppSettings
        {
            ProviderId = source.ProviderId,
            ProtocolId = source.ProtocolId,
            ProviderName = source.ProviderName,
            BaseUrl = source.BaseUrl,
            ApiKey = source.ApiKey,
            Model = source.Model,
            Temperature = source.Temperature,
            ModelContextLimit = source.ModelContextLimit,
            ModelSupportsVision = source.ModelSupportsVision,
            MaxOutputTokens = source.MaxOutputTokens,
            AgentExecutionMode = source.AgentExecutionMode,
            AgentMaxToolRounds = Math.Min(source.AgentMaxToolRounds, 8),
            EnabledToolIds = enabledToolIds,
            ToolPermissionModes = modes,
            AgentAdaptiveStrategiesEnabled = source.AgentAdaptiveStrategiesEnabled,
            AgentAdaptiveBudgetAndExplorerEnabled = source.AgentAdaptiveBudgetAndExplorerEnabled
        };
    }

    // Standard GUI mode preserves the user's enabled-tool set and permission
    // matrix. Missing values fall back to registry defaults, but an explicit
    // Disabled value or an omitted enabled ID is never silently re-enabled.
    public static AppSettings Gui(AppSettings source, AgentToolRegistry registry)
    {
        var configured = ResolveConfiguredTools(source, registry);
        var toolIds = registry.All
            .Where(tool => configured.EnabledIds.Contains(tool.Id))
            .Where(tool => configured.Modes[tool.Id] != ToolPermissionMode.Disabled)
            .Select(tool => tool.Id)
            .ToList();
        var enabledSet = toolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modes = registry.All.ToDictionary(
            tool => tool.Id,
            tool => enabledSet.Contains(tool.Id)
                ? configured.Modes[tool.Id]
                : ToolPermissionMode.Disabled,
            StringComparer.OrdinalIgnoreCase);

        return new AppSettings
        {
            ProviderId = source.ProviderId,
            ProtocolId = source.ProtocolId,
            ProviderName = source.ProviderName,
            BaseUrl = source.BaseUrl,
            ApiKey = source.ApiKey,
            Model = source.Model,
            Temperature = source.Temperature,
            ModelContextLimit = source.ModelContextLimit,
            ModelSupportsVision = source.ModelSupportsVision,
            MaxOutputTokens = source.MaxOutputTokens,
            AgentExecutionMode = source.AgentExecutionMode,
            AgentMaxToolRounds = Math.Min(source.AgentMaxToolRounds, 12),
            EnabledToolIds = toolIds,
            ToolPermissionModes = modes,
            AutoVerifyAgentRuns = source.AutoVerifyAgentRuns,
            MaxAutoFixRounds = source.MaxAutoFixRounds,
            AgentAdaptiveStrategiesEnabled = source.AgentAdaptiveStrategiesEnabled,
            AgentAdaptiveBudgetAndExplorerEnabled = source.AgentAdaptiveBudgetAndExplorerEnabled
        };
    }

    private static bool IsUpdatePlan(string toolId)
        => string.Equals(toolId, "update_plan", StringComparison.OrdinalIgnoreCase);

    private static ResolvedToolConfiguration ResolveConfiguredTools(
        AppSettings source,
        AgentToolRegistry registry)
    {
        var knownIds = registry.All.Select(tool => tool.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasExplicitConfiguration = source.EnabledToolIds.Count > 0 ||
                                       source.ToolPermissionModes.Count > 0;
        var enabledIds = hasExplicitConfiguration
            ? source.EnabledToolIds.Where(knownIds.Contains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);
        var modes = registry.All.ToDictionary(
            tool => tool.Id,
            tool => source.ToolPermissionModes.TryGetValue(tool.Id, out var configured)
                ? configured
                : registry.GetMetadata(tool.Id).DefaultPermissionMode,
            StringComparer.OrdinalIgnoreCase);

        return new ResolvedToolConfiguration(enabledIds, modes);
    }

    private sealed record ResolvedToolConfiguration(
        HashSet<string> EnabledIds,
        Dictionary<string, ToolPermissionMode> Modes);
}
