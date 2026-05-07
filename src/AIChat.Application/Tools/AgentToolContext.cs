namespace AIChat.Application.Tools;

using AIChat.Domain.Artifacts;

public sealed class AgentToolContext
{
    public required string ProjectPath { get; init; }
    public IReadOnlyList<InputArtifact> InputArtifacts { get; init; } = [];
}
