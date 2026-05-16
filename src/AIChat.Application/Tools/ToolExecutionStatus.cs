namespace AIChat.Application.Tools;

public enum ToolExecutionStatus
{
    Succeeded,
    Failed,
    Rejected,
    Disabled,
    UnknownTool,
    TimedOut,
    Cancelled,
    Exception
}
