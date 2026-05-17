using System.Text.RegularExpressions;

namespace AIChat.Application.Plugins;

internal static partial class PluginIds
{
    public static string Normalize(string value)
    {
        var normalized = IdRegex().Replace(value.Trim().ToLowerInvariant(), "_").Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("插件 id 不能为空。");
        }

        return normalized;
    }

    public static string NormalizeToolId(string pluginId, string toolId)
    {
        var normalizedToolId = Normalize(toolId);
        return normalizedToolId.StartsWith(pluginId + "_", StringComparison.OrdinalIgnoreCase)
            ? normalizedToolId
            : $"{pluginId}_{normalizedToolId}";
    }

    [GeneratedRegex("[^a-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();
}
