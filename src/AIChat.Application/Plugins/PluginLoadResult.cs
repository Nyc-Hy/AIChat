namespace AIChat.Application.Plugins;

public sealed record PluginLoadResult(
    IReadOnlyList<PluginManifest> Manifests,
    IReadOnlyList<PluginDiagnostic> Diagnostics);
