using System.Text.Json;

namespace AIChat.Application.Plugins;

public static class PluginManifestValidator
{
    public static IReadOnlyList<PluginDiagnostic> Validate(PluginManifest manifest, string? manifestPath = null)
    {
        var diagnostics = new List<PluginDiagnostic>();
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            diagnostics.Add(Error("插件 id 不能为空。", manifestPath: manifestPath));
        }

        if (manifest.Tools.Count == 0)
        {
            diagnostics.Add(Warning("插件未声明任何工具。", manifest.Id, manifestPath: manifestPath));
        }

        var seenToolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in manifest.Tools)
        {
            ValidateTool(manifest, tool, seenToolIds, diagnostics, manifestPath);
        }

        return diagnostics;
    }

    private static void ValidateTool(
        PluginManifest manifest,
        PluginToolManifest tool,
        HashSet<string> seenToolIds,
        List<PluginDiagnostic> diagnostics,
        string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(tool.Id))
        {
            diagnostics.Add(Error("工具 id 不能为空。", manifest.Id, manifestPath: manifestPath));
            return;
        }

        if (!seenToolIds.Add(tool.Id))
        {
            diagnostics.Add(Error($"工具 id 重复：{tool.Id}", manifest.Id, tool.Id, manifestPath));
        }

        if (string.IsNullOrWhiteSpace(tool.Description))
        {
            diagnostics.Add(Warning("工具缺少 description，模型可能难以正确选择它。", manifest.Id, tool.Id, manifestPath));
        }

        if (string.IsNullOrWhiteSpace(tool.Command.Executable))
        {
            diagnostics.Add(Error("命令型插件工具缺少 command.executable。", manifest.Id, tool.Id, manifestPath));
        }

        if (!IsKnownRisk(tool.Risk))
        {
            diagnostics.Add(Warning($"未知风险等级 `{tool.Risk}`，将按 shell 处理。", manifest.Id, tool.Id, manifestPath));
        }

        if (!IsJsonObject(tool.ParametersJson))
        {
            diagnostics.Add(Error("parametersJson 必须是 JSON object。", manifest.Id, tool.Id, manifestPath));
        }
    }

    private static bool IsJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsKnownRisk(string risk)
    {
        return risk.Trim().ToLowerInvariant() is
            "readonly" or "read_only" or "read-only" or "read" or
            "write" or "mutation" or
            "shell";
    }

    private static PluginDiagnostic Error(
        string message,
        string? pluginId = null,
        string? toolId = null,
        string? manifestPath = null)
    {
        return new PluginDiagnostic(PluginDiagnosticSeverity.Error, message, pluginId, toolId, manifestPath);
    }

    private static PluginDiagnostic Warning(
        string message,
        string? pluginId = null,
        string? toolId = null,
        string? manifestPath = null)
    {
        return new PluginDiagnostic(PluginDiagnosticSeverity.Warning, message, pluginId, toolId, manifestPath);
    }
}
