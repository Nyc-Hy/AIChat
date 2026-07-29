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
    // Plain chat: no tools at all, single-shot request.
    public static AppSettings Plain(AppSettings source) => new()
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
        AgentMaxToolRounds = source.AgentMaxToolRounds,
        EnabledToolIds = [],
        ToolPermissionModes = []
    };

    // Read-only mode: only read-only tools are enabled, with a tighter
    // round budget so a stuck agent doesn't burn the whole budget.
    public static AppSettings ReadOnly(AppSettings source, AgentToolRegistry registry)
    {
        var enabledToolIds = registry.All
            .Where(tool => tool.Risk == AgentToolRisk.ReadOnly || IsUpdatePlan(tool.Id))
            .Select(tool => tool.Id)
            .ToList();
        var modes = ToModeDictionary(registry, enabledToolIds,
            granted: ToolPermissionMode.AutoReadOnly,
            restricted: ToolPermissionMode.Disabled);

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
            ToolPermissionModes = modes
        };
    }

    // Standard GUI mode: all tools enabled, read-only tools auto-allowed,
    // everything else requires per-call approval. Verification and auto-fix
    // are preserved from the user settings.
    public static AppSettings Gui(AppSettings source, AgentToolRegistry registry)
    {
        var toolIds = registry.All.Select(tool => tool.Id).ToList();
        var modes = registry.All.ToDictionary(
            tool => tool.Id,
            tool => tool.Risk == AgentToolRisk.ReadOnly || IsUpdatePlan(tool.Id)
                ? ToolPermissionMode.AutoReadOnly
                : ToolPermissionMode.ConfirmEachTime,
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
            MaxAutoFixRounds = source.MaxAutoFixRounds
        };
    }

    private static bool IsUpdatePlan(string toolId)
        => string.Equals(toolId, "update_plan", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, ToolPermissionMode> ToModeDictionary(
        AgentToolRegistry registry,
        IReadOnlyCollection<string> grantedToolIds,
        ToolPermissionMode granted,
        ToolPermissionMode restricted)
    {
        var grantedSet = new HashSet<string>(grantedToolIds, StringComparer.OrdinalIgnoreCase);
        return registry.All.ToDictionary(
            tool => tool.Id,
            tool => grantedSet.Contains(tool.Id) ? granted : restricted,
            StringComparer.OrdinalIgnoreCase);
    }
}
