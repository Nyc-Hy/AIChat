using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Tools;

public sealed record ToolOptionState(
    string Id,
    string Name,
    string Description,
    AgentToolRisk Risk,
    bool IsEnabled,
    ToolPermissionMode PermissionMode);

public static class ToolSettingsService
{
    public static void Normalize(AppSettings settings, AgentToolRegistry registry)
    {
        var knownIds = registry.All.Select(tool => tool.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasExplicitConfiguration = settings.EnabledToolIds.Count > 0 ||
                                       settings.ToolPermissionModes.Count > 0;
        settings.EnabledToolIds = settings.EnabledToolIds
            .Where(knownIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!hasExplicitConfiguration)
        {
            settings.EnabledToolIds = registry.All
                .Select(tool => tool.Id)
                .ToList();
        }
        else if (settings.EnabledToolIds.Contains("git_status", StringComparer.OrdinalIgnoreCase) &&
                 settings.EnabledToolIds.Contains("git_diff", StringComparer.OrdinalIgnoreCase) &&
                 knownIds.Contains("git_restore_file"))
        {
            AddEnabledToolIfKnown(settings, knownIds, "git_restore_file");
            AddEnabledToolIfKnown(settings, knownIds, "git_commit");
        }

        settings.ToolPermissionModes = settings.ToolPermissionModes
            .Where(entry => knownIds.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var tool in registry.All)
        {
            settings.ToolPermissionModes.TryAdd(tool.Id, registry.GetMetadata(tool.Id).DefaultPermissionMode);
        }

        settings.EnabledToolIds.RemoveAll(toolId =>
            settings.ToolPermissionModes.TryGetValue(toolId, out var mode) &&
            mode == ToolPermissionMode.Disabled);
    }

    public static IReadOnlyList<ToolOptionState> CreateToolOptions(AppSettings settings, AgentToolRegistry registry)
    {
        var enabled = settings.EnabledToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return registry.AllWithMetadata()
            .Select(entry =>
            {
                var mode = settings.ToolPermissionModes.TryGetValue(entry.Tool.Id, out var configuredMode)
                    ? configuredMode
                    : entry.Metadata.DefaultPermissionMode;
                return new ToolOptionState(
                    entry.Tool.Id,
                    entry.Tool.Definition.Name,
                    entry.Tool.Definition.Description,
                    entry.Tool.Risk,
                    enabled.Contains(entry.Tool.Id),
                    mode);
            })
            .ToList();
    }

    public static void SyncToolOptions(
        AppSettings settings,
        IEnumerable<(string Id, bool IsEnabled, string PermissionMode)> toolOptions)
    {
        var options = toolOptions.ToList();
        settings.EnabledToolIds = options
            .Where(tool => tool.IsEnabled && !string.Equals(tool.PermissionMode, nameof(ToolPermissionMode.Disabled), StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.ToolPermissionModes = options.ToDictionary(
            tool => tool.Id,
            tool => Enum.TryParse<ToolPermissionMode>(tool.PermissionMode, out var mode) ? mode : ToolPermissionMode.ConfirmEachTime,
            StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, ToolPermissionMode> MergePermissionModes(
        Dictionary<string, ToolPermissionMode> global,
        Dictionary<string, string>? projectOverrides)
    {
        if (projectOverrides is null or { Count: 0 })
        {
            return global;
        }

        var merged = new Dictionary<string, ToolPermissionMode>(global, StringComparer.OrdinalIgnoreCase);
        foreach (var (toolId, modeName) in projectOverrides)
        {
            if (Enum.TryParse<ToolPermissionMode>(modeName, ignoreCase: true, out var mode))
            {
                merged[toolId] = mode;
            }
        }

        return merged;
    }

    public static Dictionary<string, string> CreateProjectOverrides(
        IEnumerable<(string ToolId, string PermissionMode)> overrides)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in overrides)
        {
            if (string.IsNullOrWhiteSpace(option.ToolId))
            {
                continue;
            }

            result[option.ToolId] = option.PermissionMode;
        }

        return result;
    }

    private static void AddEnabledToolIfKnown(AppSettings settings, HashSet<string> knownIds, string toolId)
    {
        if (knownIds.Contains(toolId) &&
            (!settings.ToolPermissionModes.TryGetValue(toolId, out var mode) || mode != ToolPermissionMode.Disabled) &&
            !settings.EnabledToolIds.Contains(toolId, StringComparer.OrdinalIgnoreCase))
        {
            settings.EnabledToolIds.Add(toolId);
        }
    }
}
