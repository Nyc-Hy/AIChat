using AIChat.Application.Agents;

namespace AIChat.App.ViewModels;

public sealed class PendingToolApprovalViewModel : ObservableObject
{
    private readonly TaskCompletionSource<ToolApprovalDecision> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingToolApprovalViewModel(ToolApprovalRequest request)
    {
        Request = request;
    }

    public ToolApprovalRequest Request { get; }
    public string ToolName => Request.Preview.ToolName;
    public string Summary => Request.Preview.Summary;
    public string ArgumentsJson => Request.ToolCall.ArgumentsJson;
    public string PreviewText => Request.Preview.PreviewText;
    public string DiffText => Request.Preview.DiffText;
    public bool HasDiff => !string.IsNullOrWhiteSpace(DiffText);
    public Task<ToolApprovalDecision> Completion => _completion.Task;

    public void Approve(bool allowForSession)
    {
        _completion.TrySetResult(ToolApprovalDecision.Approve(allowForSession));
    }

    public void Reject()
    {
        _completion.TrySetResult(ToolApprovalDecision.Reject("用户在界面中拒绝。"));
    }
}
