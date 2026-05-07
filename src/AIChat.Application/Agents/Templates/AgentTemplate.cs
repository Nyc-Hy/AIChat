using AIChat.Abstractions.Configuration;
using AIChat.Application.Prompting;

namespace AIChat.Application.Agents.Templates;

public sealed class AgentTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required AgentPromptProfile PromptProfile { get; init; }
    public IReadOnlyList<string> DefaultToolIds { get; init; } = [];
    public IReadOnlyDictionary<string, ToolPermissionMode> DefaultToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
    public bool CanRead { get; init; } = true;
    public bool CanWrite { get; init; }
    public bool CanVerify { get; init; }
    public string OutputSchema { get; init; } = "";

    public IReadOnlyDictionary<string, ToolPermissionMode> BuildPermissionModes()
    {
        var modes = new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolId in DefaultToolIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            modes[toolId] = DefaultToolPermissionModes.TryGetValue(toolId, out var mode)
                ? mode
                : ToolPermissionMode.ConfirmEachTime;
        }

        return modes;
    }
}
