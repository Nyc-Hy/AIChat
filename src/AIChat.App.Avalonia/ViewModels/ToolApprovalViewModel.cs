using AIChat.Application.Agents;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the right-rail "tool approval" surface: the pending decision state,
// the Approve / Reject commands, and the TaskCompletionSource that bridges
// the agent-run thread (the awaiter) to the UI thread (the user).
//
// PR-6 scope: pure extraction from MainWindowViewModel. The view-model is
// the single owner of the TCS — the IApprovalService implementation
// (UIBoundApprovalService) is a thin facade that delegates here. The
// parent subscribes to RequestPresented to mirror the request in its
// own Activity / StatusMessage surface.
public sealed partial class ToolApprovalViewModel : ViewModelBase
{
    private TaskCompletionSource<ToolApprovalDecision>? _pending;
    private readonly object _gate = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectCommand))]
    private bool hasPendingApproval;

    [ObservableProperty]
    private string pendingApprovalTitle = "";

    [ObservableProperty]
    private string pendingApprovalSummary = "";

    [ObservableProperty]
    private string pendingApprovalPreview = "";

    // Set by the parent whenever the user toggles "只读模式" in the
    // safety panel. Short-circuits the approval request to an instant
    // reject so the agent harness never even shows the dialog.
    [ObservableProperty]
    private bool isReadOnly;

    public event EventHandler<ToolApprovalPresentedEventArgs>? RequestPresented;
    public event EventHandler<ToolApprovalResolvedEventArgs>? RequestResolved;

    // The single entry point used by the approval service. Thread-safe:
    // may be called from any thread (typically the agent run's background
    // continuation). Posts UI state changes onto the dispatcher and returns
    // a task that completes when the user clicks Approve or Reject.
    public Task<ToolApprovalDecision> PresentRequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken)
    {
        if (IsReadOnly)
        {
            return Task.FromResult(ToolApprovalDecision.Reject("只读模式已开启。"));
        }

        TaskCompletionSource<ToolApprovalDecision> completion;
        lock (_gate)
        {
            // Cancel any in-flight request so we never strand an awaiter.
            _pending?.TrySetCanceled();
            _pending = completion = new TaskCompletionSource<ToolApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, completion))
                {
                    _pending = null;
                }
            }
            completion.TrySetCanceled(cancellationToken);
        });

        Dispatcher.UIThread.Post(() =>
        {
            PendingApprovalTitle = FriendlyApprovalTitle(request.ToolCall.Name);
            PendingApprovalSummary = request.Preview.Summary;
            PendingApprovalPreview = FirstNonBlank(
                request.Preview.PreviewText,
                request.Preview.DiffText,
                "允许前请先查看此操作。");
            HasPendingApproval = true;
            RequestPresented?.Invoke(this, new ToolApprovalPresentedEventArgs
            {
                Request = request,
                StatusMessage = "正在等待你的确认。"
            });
        });

        return completion.Task;
    }

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private void Approve() => Resolve(ToolApprovalDecision.Approve());

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private void Reject() => Resolve(ToolApprovalDecision.Reject("已在界面中拒绝。"));

    private bool CanResolve() => HasPendingApproval;

    private void Resolve(ToolApprovalDecision decision)
    {
        TaskCompletionSource<ToolApprovalDecision>? completion;
        lock (_gate)
        {
            completion = _pending;
            _pending = null;
        }

        HasPendingApproval = false;
        PendingApprovalTitle = "";
        PendingApprovalSummary = "";
        PendingApprovalPreview = "";

        completion?.TrySetResult(decision);
        RequestResolved?.Invoke(this, new ToolApprovalResolvedEventArgs { Decision = decision });
    }

    private static string FriendlyApprovalTitle(string toolName) => toolName switch
    {
        "write_file" => "允许写入文件？",
        "edit_file" => "允许编辑文件？",
        "apply_patch" => "允许应用补丁？",
        "run_build" => "允许运行构建？",
        "run_test" => "允许运行测试？",
        "run_shell" => "允许运行命令？",
        "git_restore_file" => "允许还原文件？",
        "git_commit" => "允许创建 Git 提交？",
        _ => $"允许执行 {toolName}？"
    };

    private static string FirstNonBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
