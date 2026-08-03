using AIChat.Application.Agents;
using AIChat.App.Avalonia.ViewModels;

namespace AIChat.App.Avalonia.Composition;

// Default IApprovalService implementation. Delegates to ToolApprovalViewModel,
// which owns the actual UI state and the TaskCompletionSource that bridges
// the agent run thread to the UI thread.
//
// State lives in the view-model, not the service — the service is just a
// thin facade so the agent harness can stay UI-agnostic. Re-entry (a new
// approval request before the previous one resolves) is handled by
// ToolApprovalViewModel.PresentRequestAsync, which cancels the previous
// completion source if it is still pending.
public sealed class UIBoundApprovalService : IApprovalService
{
    private readonly ToolApprovalViewModel _viewModel;

    public UIBoundApprovalService(ToolApprovalViewModel viewModel)
    {
        _viewModel = viewModel;
        // Forward the view-model's countdown-firing event to the
        // service's own surface so consumers (the cron engine in
        // particular) can subscribe to IApprovalService without
        // taking a direct dependency on the Avalonia view-model.
        _viewModel.UnattendedTimeoutFired += (_, _) => UnattendedTimeoutFired?.Invoke(this, EventArgs.Empty);
    }

    public Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken)
        => _viewModel.PresentRequestAsync(request, cancellationToken);

    public void RejectPendingIfAny(string reason)
        => _viewModel.RejectPendingIfAny(reason);

    public void StartUnattendedCountdown(TimeSpan timeout)
        => _viewModel.StartUnattendedCountdown(timeout);

    public event EventHandler? UnattendedTimeoutFired;
}
