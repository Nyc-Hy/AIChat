using AIChat.Application.Context;

namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ParentRunId { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string Task { get; init; } = "";
    public TaskContextPack? ContextPack { get; init; }
    public int MaxToolCalls { get; init; }
    public int ToolCallCount { get; set; }
    public SubAgentStatus Status { get; set; } = SubAgentStatus.Running;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<SubAgentToolCallRecord> ToolCalls { get; init; } = [];
    public SubAgentResult? Result { get; set; }
}
