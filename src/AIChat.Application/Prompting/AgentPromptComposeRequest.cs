using AIChat.Domain.Chat;

namespace AIChat.Application.Prompting;

public sealed class AgentPromptComposeRequest
{
    public required AgentPromptProfile Profile { get; init; }
    public string Goal { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public string Model { get; init; } = "";
    public SystemPromptContext? SystemContext { get; init; }
    public AgentStructuredPlan? Plan { get; init; }
    public AgentPlanBudget? Budget { get; init; }
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> ContextRefs { get; init; } = [];
    public IReadOnlyList<string> InputArtifactRefs { get; init; } = [];
    public IReadOnlyList<string> MemorySnippets { get; init; } = [];
    public IReadOnlyList<ChatMessage> ConversationMessages { get; init; } = [];
    public string FailureSummary { get; init; } = "";
    public string ResponseRequirements { get; init; } = "";
}
