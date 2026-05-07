using AIChat.Abstractions.Configuration;
using AIChat.Application.Context;

namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentRunRequest
{
    public required string ParentRunId { get; init; }
    public required string Task { get; init; }
    public required string ProjectPath { get; init; }
    public required AppSettings Settings { get; init; }
    public string TemplateId { get; init; } = "explorer";
    public TaskContextPack? ContextPack { get; init; }
    public int MaxToolCalls { get; init; } = 4;
    public IReadOnlyList<string> WriteScope { get; init; } = [];
}
