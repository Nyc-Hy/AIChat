using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Prompting;

public sealed class SystemPromptContext
{
    public string ProjectName { get; init; } = "AIChat";
    public string ProjectPath { get; init; } = "";
    public IReadOnlyList<string> EnabledToolIds { get; init; } = [];
    public IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
}
