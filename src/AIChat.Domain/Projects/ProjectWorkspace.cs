using AIChat.Domain.Chat;
using AIChat.Domain.Context;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Memory;

namespace AIChat.Domain.Projects;

// v0 schema container. Wave 3: replaced everywhere in the UI / domain
// layer by WorkspaceProject + ChatSession. The type still exists
// because JsonAppRepository's v0→v1 migration path (LoadProjectsCoreV0Async
// + MigrationCoordinator) reads v0 projects.json off disk and feeds
// them to V0ToV1Converter.Convert. After migration, this type is no
// longer constructed or used in any other code path — it's a private
// shape for the one-way v0→v1 bridge.
//
// New UI / domain code MUST NOT take a dependency on this type.
// Use WorkspaceProject instead.
[Obsolete("v0 schema container. Use WorkspaceProject for all new code. Kept only so JsonAppRepository's v0→v1 migration can read legacy projects.json files.", false)]
public sealed class ProjectWorkspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "AIChat";
    public string Path { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<Conversation> Conversations { get; set; } = [];
    public List<PinnedContextItem> PinnedContext { get; set; } = [];
    public List<InputArtifact> InputArtifacts { get; set; } = [];
    public List<MemoryEntry> Memories { get; set; } = [];
    public List<MemoryEntry> PendingMemories { get; set; } = [];
    public List<ProjectVerificationCommand> VerificationCommands { get; set; } = [];
    // Per-project tool permission overrides (tool ID -> mode name).
    // Merged with global AppSettings.ToolPermissionModes; project values take precedence.
    public Dictionary<string, string> ProjectToolPermissionModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
