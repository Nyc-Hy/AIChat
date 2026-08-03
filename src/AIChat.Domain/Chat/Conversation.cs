namespace AIChat.Domain.Chat;

// v0 schema container for a single conversation. Wave 3: replaced
// everywhere in the UI / domain layer by ChatSession (with the
// Project / Standalone polymorphic split). The type still exists
// because JsonAppRepository's v0→v1 migration path (V0ToV1Converter)
// reads v0 conversations off disk and maps each one to a Project
// ChatSession. After migration, this type is no longer constructed
// or used in any other code path — it's a private shape for the
// one-way v0→v1 bridge.
//
// New UI / domain code MUST NOT take a dependency on this type.
// Use ChatSession instead.
[Obsolete("v0 schema container. Use ChatSession for all new code. Kept only so JsonAppRepository's v0→v1 migration can read legacy conversations off disk.", false)]
public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "新对话";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = [];
    // These records are not part of the model context; they exist for learning,
    // debugging, and later observability.
    public List<LlmCallDetail> CallDetails { get; set; } = [];
    public List<AgentRun> AgentRuns { get; set; } = [];
}
