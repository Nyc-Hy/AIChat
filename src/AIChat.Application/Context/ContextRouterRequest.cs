using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;

namespace AIChat.Application.Context;

public sealed class ContextRouterRequest
{
    public string Goal { get; init; } = "";
    public AgentRunPhase Phase { get; init; } = AgentRunPhase.Executing;
    public ProjectFileIndex? FileIndex { get; init; }
    public IReadOnlyList<PinnedContextItem> PinnedItems { get; init; } = [];
    public IReadOnlyList<ChatMessage> ConversationMessages { get; init; } = [];
    public IReadOnlyList<WorkspaceChange> WorkspaceChanges { get; init; } = [];
    public IReadOnlyList<AgentArtifact> Artifacts { get; init; } = [];
    public int MaxTokens { get; init; } = 1200;
    public long MaxFileSizeBytes { get; init; } = 256 * 1024;
}
