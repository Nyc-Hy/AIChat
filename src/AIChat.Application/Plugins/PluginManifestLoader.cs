using System.Text.Json;

namespace AIChat.Application.Plugins;

public static class PluginManifestLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<IReadOnlyList<PluginManifest>> LoadDirectoryAsync(
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = await LoadDirectoryWithDiagnosticsAsync(pluginsDirectory, cancellationToken);
        return result.Manifests;
    }

    public static async Task<PluginLoadResult> LoadDirectoryWithDiagnosticsAsync(
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginsDirectory) || !Directory.Exists(pluginsDirectory))
        {
            return new PluginLoadResult([], []);
        }

        var manifests = new List<PluginManifest>();
        var diagnostics = new List<PluginDiagnostic>();
        foreach (var manifestPath in Directory.EnumerateFiles(pluginsDirectory, "plugin.json", SearchOption.AllDirectories))
        {
            PluginManifest? manifest;
            try
            {
                manifest = await LoadFileAsync(manifestPath, cancellationToken);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new PluginDiagnostic(
                    PluginDiagnosticSeverity.Error,
                    $"插件 manifest 读取失败：{ex.Message}",
                    ManifestPath: manifestPath));
                continue;
            }

            if (manifest is null)
            {
                diagnostics.Add(new PluginDiagnostic(
                    PluginDiagnosticSeverity.Error,
                    "插件 manifest 内容为空。",
                    ManifestPath: manifestPath));
                continue;
            }

            var manifestDiagnostics = PluginManifestValidator.Validate(manifest, manifestPath);
            diagnostics.AddRange(manifestDiagnostics);
            if (manifestDiagnostics.Any(diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error))
            {
                continue;
            }

            if (manifest.Enabled)
            {
                manifests.Add(manifest);
            }
        }

        return new PluginLoadResult(manifests, diagnostics);
    }

    public static async Task<PluginManifest?> LoadFileAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream, Options, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        Normalize(manifest, Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? "");
        return manifest;
    }

    private static void Normalize(PluginManifest manifest, string manifestDirectory)
    {
        manifest.Id = PluginIds.Normalize(manifest.Id);
        manifest.Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name.Trim();
        manifest.DirectoryPath = manifestDirectory;
        foreach (var tool in manifest.Tools)
        {
            tool.Id = PluginIds.NormalizeToolId(manifest.Id, tool.Id);
            tool.Name = string.IsNullOrWhiteSpace(tool.Name) ? tool.Id : tool.Name.Trim();
            tool.Description = string.IsNullOrWhiteSpace(tool.Description)
                ? $"插件工具：{tool.Name}"
                : tool.Description.Trim();
            if (string.IsNullOrWhiteSpace(tool.Command.WorkingDirectory))
            {
                tool.Command.WorkingDirectory = manifestDirectory;
            }
        }
    }
}
