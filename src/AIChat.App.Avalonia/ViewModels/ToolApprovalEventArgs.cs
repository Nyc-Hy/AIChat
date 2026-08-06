using AIChat.Application.Agents;

namespace AIChat.App.Avalonia.ViewModels;

// Raised by ToolApprovalViewModel when a new approval request is presented
// to the user. The parent uses this to add a "需要确认" activity entry
// and to surface the request in the status line.
public sealed class ToolApprovalPresentedEventArgs : EventArgs
{
    public required ToolApprovalRequest Request { get; init; }
    public required string StatusMessage { get; init; }
}

// Raised by ToolApprovalViewModel when the user clicks Approve or Reject.
// The parent uses this to add a "已允许" / "已拒绝" activity entry.
public sealed class ToolApprovalResolvedEventArgs : EventArgs
{
    public required ToolApprovalDecision Decision { get; init; }
}
