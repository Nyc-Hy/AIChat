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

    // 1.0.1: arm the unattended auto-reject countdown on the
    // current pending approval (if any). The cron engine calls
    // this when a background run lands on an approval gate so
    // the modal shows a "auto-reject in Ns" hint and the run
    // doesn't strand on screen if the user walks away. UI-
    // initiated approvals (composer send) do NOT call this —
    // the user is at the window and an auto-reject would be
    // confusing. The desktop host forwards to the
    // ToolApprovalViewModel; no-op when no approval is pending
    // (a stray call from a run that already resolved or
    // never landed on a gate doesn't surface stale state).
    void StartUnattendedCountdown(TimeSpan timeout);

    // 1.0.1: raised when the unattended-countdown timer hit zero
    // and auto-rejected the pending approval. The cron engine
    // subscribes to this so it can record the run as Failed with
    // a clear "无人值守 timeout" message in the run history (a
    // user-driven Reject looks the same as an auto-reject from
    // the agent's point of view — both end with the same tool
    // error and the same "已完成" status — so the cron engine
    // needs an explicit signal to override the status).
    event EventHandler? UnattendedTimeoutFired;
}
