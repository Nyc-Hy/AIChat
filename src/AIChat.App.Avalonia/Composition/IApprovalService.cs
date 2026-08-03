using AIChat.Application.Agents;

namespace AIChat.App.Avalonia.Composition;

// Boundary between the agent harness and whatever UI surface answers
// tool-approval prompts. The harness depends only on this interface so
// future hosts (CLI, headless tests, A2A server) can plug in their own
// implementation without dragging in the Avalonia window.
public interface IApprovalService
{
    Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken);

    // 1.0.1: forced-reject the current pending approval
    // (if any). The cron engine calls this when a
    // background run has been waiting for an approval
    // longer than the unattended-timeout — the
    // approval modal sits on screen until the user
    // returns, which would otherwise strand the
    // scheduled run. The desktop host (the only
    // implementation) forwards to the
    // ToolApprovalViewModel; no-op when no request
    // is pending so a stray timeout from a non-
    // background run can't accidentally reject a
    // user-initiated approval that's in flight.
    void RejectPendingIfAny(string reason);
}
