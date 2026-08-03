using AIChat.Abstractions.Configuration;
using AIChat.Application.Workspace;
using AIChat.Domain.Context;

namespace AIChat.Application.Prompting;

public sealed class SystemPromptContext
{
    // 2026-08-02: catalog is MiniMax only. The default here is
    // just a shape placeholder — callers (AgentRunner / harness)
    // pass the real provider id from AppSettings at build time.
    // The legacy "tokenplan-mimo" string is no longer recognised
    // by ChatProviderCatalog.Resolve (it falls through to MiniMax
    // via the catalog's unknown-input fallback), so an unwritten
    // default that landed here would silently look up the
    // MiniMax ModelProfile. That's the right outcome, but having
    // the default say "minimax" matches the rest of the codebase
    // (AppSettings, JsonAppRepository.CreateInitialSettings) and
    // keeps grep results clean.
    public string ProviderId { get; init; } = "minimax";
    public string ProjectName { get; init; } = "AIChat";
    public string ProjectPath { get; init; } = "";
    public string ProjectLoadSnapshot { get; init; } = "";
    public string ProjectPreparationSummary { get; init; } = "";
    public IReadOnlyList<string> EnabledToolIds { get; init; } = [];
    public IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
    public ProjectFileIndex? FileIndex { get; init; }
    public string WorkspaceSummary { get; init; } = "";
    public IReadOnlyList<PinnedContextItem> PinnedContextItems { get; init; } = [];
    public IReadOnlyList<string> ContextRefs { get; init; } = [];
    public IReadOnlyList<string> MemorySnippets { get; init; } = [];
    public IReadOnlyList<string> InputArtifactRefs { get; init; } = [];
    public string ExecutionMode { get; init; } = "Standard";
    public string ModelProfileName { get; init; } = "";
    public string ModelProfilePromptGuidance { get; init; } = "";
    public string ModelProfileToolCallPolicy { get; init; } = "";
    public string ModelProfileThinkingPolicy { get; init; } = "";
    public string ModelProfileCacheStrategy { get; init; } = "";
}
