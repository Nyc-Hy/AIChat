namespace AIChat.Domain.Chat;

public sealed class AgentSubAgentToolCall
{
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string ArgumentsJson { get; set; } = "";
    public bool IsError { get; set; }
    public string ResultSummary { get; set; } = "";
}
