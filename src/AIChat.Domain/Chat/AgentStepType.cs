namespace AIChat.Domain.Chat;

public enum AgentStepType
{
    Model,
    ToolCall,
    ToolResult,
    Approval,
    Final
}
