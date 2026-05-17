namespace AIChat.Application.Plugins;

public sealed record PluginDiagnostic(
    PluginDiagnosticSeverity Severity,
    string Message,
    string? PluginId = null,
    string? ToolId = null,
    string? ManifestPath = null);
