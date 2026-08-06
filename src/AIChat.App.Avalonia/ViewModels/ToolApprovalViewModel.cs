using AIChat.Application.Agents;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the window-modal tool approval surface: the pending decision state,
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
    private TaskCompletionSource<ToolApprovalDecision>? _presented;
    private DispatcherTimer? _unattendedTimer;
    private readonly object _gate = new();

    // 1.0.1: seconds remaining until the unattended auto-reject fires.
    // Bound by the modal so the user can see the countdown when the
    // background scheduler is the one that parked the run on this gate.
    // 0 means the countdown isn't active (i.e. a user-initiated
    // approval that the cron engine isn't watching over). The modal
    // hides the "auto-reject in Ns" line when this is 0 / the
    // IsUnattendedCountdownActive flag is false.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnattendedCountdownActive))]
    private int unattendedSecondsRemaining;

    public bool IsUnattendedCountdownActive => UnattendedSecondsRemaining > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveForSessionCommand))]
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

    // 1.0.1: fired when the unattended-countdown timer hit zero
    // and auto-rejected the pending approval. The cron engine
    // subscribes to this so it can record the run as Failed with
    // a clear "无人值守 timeout" message in the run history (a
    // user-driven Reject looks the same as an auto-reject from
    // the agent's point of view — both end with the same tool
    // error and the same "已完成" status — so the cron engine
    // needs an explicit signal to override the status).
    public event EventHandler? UnattendedTimeoutFired;

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
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ToolApprovalDecision>(cancellationToken);
        }

        TaskCompletionSource<ToolApprovalDecision> completion;
        TaskCompletionSource<ToolApprovalDecision>? superseded;
        lock (_gate)
        {
            // Cancel any in-flight request so we never strand an awaiter.
            superseded = _pending;
            _pending = completion = new TaskCompletionSource<ToolApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        superseded?.TrySetCanceled();

        var cancellationRegistration = cancellationToken.Register(
            () => CancelPending(completion, cancellationToken));
        _ = completion.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        RunOnUiThread(() =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_pending, completion) ||
                    completion.Task.IsCompleted ||
                    cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                _presented = completion;
            }

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

    private void CancelPending(
        TaskCompletionSource<ToolApprovalDecision> completion,
        CancellationToken cancellationToken)
    {
        bool wasCurrent;
        bool wasPresented;
        lock (_gate)
        {
            wasCurrent = ReferenceEquals(_pending, completion);
            wasPresented = ReferenceEquals(_presented, completion);
            if (wasCurrent)
            {
                _pending = null;
            }
            if (wasPresented)
            {
                _presented = null;
            }
        }

        completion.TrySetCanceled(cancellationToken);
        if (!wasCurrent)
        {
            return;
        }

        StopUnattendedCountdown();

        RunOnUiThread(() =>
        {
            lock (_gate)
            {
                // A newer request owns the surface now; never clear it from an
                // older cancellation callback.
                if (_pending is not null)
                {
                    return;
                }
            }

            ClearPendingSurface();
            if (wasPresented)
            {
                RequestResolved?.Invoke(this, new ToolApprovalResolvedEventArgs
                {
                    Decision = ToolApprovalDecision.Reject("任务已取消。")
                });
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private void Approve() => Resolve(ToolApprovalDecision.Approve());

    // "Allow for this session" — the same tool (same ToolCall.Id
    // matches, but in practice any write of the same name) is allowed
    // for the rest of the run. The agent loop turns this into a
    // SessionAllowed event and the tool's preflight check skips the
    // approval request on subsequent calls. Saves the user from
    // approving five read_file calls in a row, or a long edit_file
    // run that needs to make many writes.
    [RelayCommand(CanExecute = nameof(CanResolve))]
    private void ApproveForSession() => Resolve(ToolApprovalDecision.Approve(allowForSession: true));

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private void Reject() => Resolve(ToolApprovalDecision.Reject("已在界面中拒绝。"));

    // 1.0.1: forced-reject the current pending
    // approval, called by the IApprovalService when
    // the cron engine's unattended-timeout fires.
    // No-op when no approval is pending so a stray
    // timeout from a non-background run can't
    // accidentally reject a user-initiated approval
    // that's in flight. The reason lands in the
    // run-history message so the user sees
    // "auto-rejected (无人值守 timeout)" when they
    // come back.
    public void RejectPendingIfAny(string reason)
    {
        TaskCompletionSource<ToolApprovalDecision>? completion;
        lock (_gate)
        {
            completion = _pending;
        }
        if (completion is null)
        {
            return;
        }
        StopUnattendedCountdown();
        Resolve(ToolApprovalDecision.Reject(reason));
    }

    // 1.0.1: arm the unattended auto-reject countdown. The cron engine
    // calls this when a background run lands on an approval gate so the
    // user has a visible "auto-reject in Ns" hint in the modal and the
    // run doesn't strand on screen if they walk away. UI-initiated
    // approvals (composer send) don't call this — the user is at the
    // window and an auto-reject would be confusing. Idempotent: a
    // second call before the first fires resets the deadline (used
    // when the user clears a prior pending and a new one comes in).
    public void StartUnattendedCountdown(TimeSpan timeout)
    {
        StopUnattendedCountdown();

        var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
        UnattendedSecondsRemaining = seconds;

        _unattendedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unattendedTimer.Tick += OnUnattendedTick;
        _unattendedTimer.Start();
    }

    private void OnUnattendedTick(object? sender, EventArgs e)
    {
        // The user may have already resolved the request between
        // timer ticks (Approve / Reject clicked, modal scrim
        // clicked, an explicit CancelPending fired). In that case
        // _pending is null and Resolve below is a no-op — the
        // dispatcher keeps ticking for at most one extra interval
        // before we stop ourselves.
        if (_pending is null)
        {
            StopUnattendedCountdown();
            return;
        }

        var next = UnattendedSecondsRemaining - 1;
        if (next <= 0)
        {
            StopUnattendedCountdown();
            // Tell the cron engine before Resolve so the engine's
            // "is this an auto-reject?" flag is set before the
            // status-update side effects land. Reason: by the time
            // Resolve clears the surface and lets the agent run
            // continue, the engine has already left its poll loop
            // and is reading the final state — without the event
            // the engine can't tell a user-driven Reject from a
            // timer-driven one.
            UnattendedTimeoutFired?.Invoke(this, EventArgs.Empty);
            // Auto-reject with the same reason the cron engine would
            // have used, so the user sees the same "auto-rejected
            // (无人值守 timeout)" message in the run history either
            // way. The agent's tool call errors out, the run ends,
            // and the cron executor records the run as Failed.
            RejectPendingIfAny("auto-rejected (无人值守 timeout)");
            return;
        }

        UnattendedSecondsRemaining = next;
    }

    private void StopUnattendedCountdown()
    {
        if (_unattendedTimer is null)
        {
            return;
        }
        _unattendedTimer.Stop();
        _unattendedTimer.Tick -= OnUnattendedTick;
        _unattendedTimer = null;
        UnattendedSecondsRemaining = 0;
    }

    private bool CanResolve() => HasPendingApproval;

    private void Resolve(ToolApprovalDecision decision)
    {
        TaskCompletionSource<ToolApprovalDecision>? completion;
        lock (_gate)
        {
            completion = _pending;
            _pending = null;
            _presented = null;
        }

        StopUnattendedCountdown();
        ClearPendingSurface();

        completion?.TrySetResult(decision);
        RequestResolved?.Invoke(this, new ToolApprovalResolvedEventArgs { Decision = decision });
    }

    private void ClearPendingSurface()
    {
        HasPendingApproval = false;
        PendingApprovalTitle = "";
        PendingApprovalSummary = "";
        PendingApprovalPreview = "";
    }

    private static void RunOnUiThread(Action action)
    {
        // Unit-test and non-visual hosts do not own a live Avalonia
        // Application. In that case there is no UI thread to marshal to and
        // executing inline keeps the view-model contract deterministic.
        if (global::Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
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
