namespace AIChat.Application.Agents;

public sealed class ToolApprovalDecision
{
    public bool IsApproved { get; init; }
    public bool AllowForSession { get; init; }
    public string Reason { get; init; } = "";

    public static ToolApprovalDecision Approve(bool allowForSession = false) => new()
    {
        IsApproved = true,
        AllowForSession = allowForSession
    };

    public static ToolApprovalDecision Reject(string reason) => new()
    {
        IsApproved = false,
        Reason = reason
    };
}
