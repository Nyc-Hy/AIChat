using AIChat.Abstractions.Configuration;
using AIChat.Application.Workspace;
using AIChat.Domain.Context;

namespace AIChat.Application.Prompting;

public sealed class SystemPromptContext
{
    public string ProviderId { get; init; } = "tokenplan-mimo";
    public string ProjectName { get; init; } = "AIChat";
    public string ProjectPath { get; init; } = "";
    public IReadOnlyList<string> EnabledToolIds { get; init; } = [];
    public IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
    public ProjectFileIndex? FileIndex { get; init; }
    public string WorkspaceSummary { get; init; } = "";
    public IReadOnlyList<PinnedContextItem> PinnedContextItems { get; init; } = [];
}
