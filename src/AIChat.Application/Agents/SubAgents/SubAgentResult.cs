namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentResult
{
    public SubAgentStatus Status { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> Findings { get; init; } = [];
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlyList<string> ArtifactRefs { get; init; } = [];
    public string RecommendedNextStep { get; init; } = "";
}
