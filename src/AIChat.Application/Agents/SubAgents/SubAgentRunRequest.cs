using AIChat.Abstractions.Configuration;
using AIChat.Application.Context;
using AIChat.Domain.Artifacts;

namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentRunRequest
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public required string ParentRunId { get; init; }
    public required string Task { get; init; }
    public required string ProjectPath { get; init; }
    public required AppSettings Settings { get; init; }
    public string TemplateId { get; init; } = "explorer";
    public TaskContextPack? ContextPack { get; init; }
    public int MaxToolCalls { get; init; } = 4;
    public IReadOnlyList<string> WriteScope { get; init; } = [];
    public IReadOnlyList<InputArtifact> InputArtifacts { get; init; } = [];
}
