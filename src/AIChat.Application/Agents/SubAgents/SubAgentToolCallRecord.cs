namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentToolCallRecord
{
    public string ParentRunId { get; init; } = "";
    public string SubAgentRunId { get; init; } = "";
    public string ToolCallId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string ArgumentsJson { get; init; } = "";
    public bool IsError { get; set; }
    public string ResultSummary { get; set; } = "";
}
