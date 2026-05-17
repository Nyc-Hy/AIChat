namespace AIChat.Application.Plugins;

public sealed record PluginSkill(
    string PluginId,
    string Id,
    string Name,
    string Description,
    string Content,
    string SourcePath);
