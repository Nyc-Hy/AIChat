using AIChat.Application.Agents;

namespace AIChat.App.Avalonia.Composition;

// Boundary between the agent harness and whatever UI surface answers
// tool-approval prompts. The harness depends only on this interface so
// future hosts (CLI, headless tests, A2A server) can plug in their own
// implementation without dragging in the Avalonia window.
public interface IApprovalService
{
    Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken);
}
