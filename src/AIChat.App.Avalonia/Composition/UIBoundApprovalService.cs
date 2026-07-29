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
    }

    public Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken)
        => _viewModel.PresentRequestAsync(request, cancellationToken);
}
