using System.Reflection;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Avalonia;

// PR-6 tests. ToolApprovalViewModel.PresentRequestAsync posts UI updates
// onto Avalonia's dispatcher, which the test process does not pump. The
// tests therefore only exercise the parts of the contract that can run
// without a live dispatcher loop: the read-only short-circuit, the
// CanExecute wiring, the command resolution path, and the event
// publication on resolve.
public class ToolApprovalViewModelTests
{
    [Fact]
    public async Task IsReadOnly_WhenTrue_RejectsWithoutShowingDialog()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = true };
        var request = NewRequest("write_file");

        var decision = await vm.PresentRequestAsync(request, CancellationToken.None);

        Assert.False(decision.IsApproved);
        Assert.Equal("只读模式已开启。", decision.Reason);
        Assert.False(vm.HasPendingApproval);
    }

    [Fact]
    public void ApproveAndRejectCommands_AreDisabledBeforeAnyRequest()
    {
        var vm = new ToolApprovalViewModel();

        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.False(vm.RejectCommand.CanExecute(null));
    }

    [Fact]
    public void ApproveAndRejectCommands_BecomeEnabledWhenHasPendingApprovalIsTrue()
    {
        var vm = new ToolApprovalViewModel();
        // Force the "showing" state without driving the dispatcher — the
        // CanExecute wiring is a pure function of HasPendingApproval, so
        // toggling it directly is sufficient for this assertion.
        vm.GetType().GetProperty(nameof(vm.HasPendingApproval))!
            .SetValue(vm, true);

        Assert.True(vm.ApproveCommand.CanExecute(null));
        Assert.True(vm.RejectCommand.CanExecute(null));
    }

    [Fact]
    public async Task RejectCommand_ResolvesWithRejection()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = false };
        var request = NewRequest("write_file");

        var task = vm.PresentRequestAsync(request, CancellationToken.None);
        vm.RejectCommand.Execute(null);

        var decision = await task;
        Assert.False(decision.IsApproved);
        Assert.Equal("已在界面中拒绝。", decision.Reason);
        Assert.False(vm.HasPendingApproval);
    }

    [Fact]
    public async Task ApproveCommand_ResolvesWithApproval()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = false };
        var request = NewRequest("write_file");

        var task = vm.PresentRequestAsync(request, CancellationToken.None);
        vm.ApproveCommand.Execute(null);

        var decision = await task;
        Assert.True(decision.IsApproved);
        Assert.False(decision.AllowForSession);
        Assert.False(vm.HasPendingApproval);
    }

    [Fact]
    public async Task ApproveForSessionCommand_ResolvesWithSessionAllow()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = false };
        var request = NewRequest("write_file");

        var task = vm.PresentRequestAsync(request, CancellationToken.None);
        vm.ApproveForSessionCommand.Execute(null);

        var decision = await task;
        Assert.True(decision.IsApproved);
        Assert.True(decision.AllowForSession);
        Assert.False(vm.HasPendingApproval);
    }

    [Fact]
    public async Task Resolve_ClearsPendingTitleAndSummary()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = false };
        var request = NewRequest("run_shell");
        // Set the UI fields directly to simulate a "shown" dialog; the
        // dispatcher-driven population is not exercised in unit tests.
        vm.GetType().GetProperty(nameof(vm.PendingApprovalTitle))!
            .SetValue(vm, "允许运行命令？");
        vm.GetType().GetProperty(nameof(vm.PendingApprovalSummary))!
            .SetValue(vm, "Run dotnet build");
        vm.GetType().GetProperty(nameof(vm.PendingApprovalPreview))!
            .SetValue(vm, "dotnet build");
        vm.GetType().GetProperty(nameof(vm.HasPendingApproval))!
            .SetValue(vm, true);

        var task = vm.PresentRequestAsync(request, CancellationToken.None);
        vm.ApproveCommand.Execute(null);
        await task;

        Assert.Equal("", vm.PendingApprovalTitle);
        Assert.Equal("", vm.PendingApprovalSummary);
        Assert.Equal("", vm.PendingApprovalPreview);
    }

    [Fact]
    public async Task RequestResolved_Event_FiresOnApproveAndReject()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = false };
        var captured = new List<ToolApprovalResolvedEventArgs>();
        vm.RequestResolved += (_, args) => captured.Add(args);

        var t1 = vm.PresentRequestAsync(NewRequest("write_file"), CancellationToken.None);
        vm.ApproveCommand.Execute(null);
        await t1;

        var t2 = vm.PresentRequestAsync(NewRequest("edit_file"), CancellationToken.None);
        vm.RejectCommand.Execute(null);
        await t2;

        Assert.Equal(2, captured.Count);
        Assert.True(captured[0].Decision.IsApproved);
        Assert.False(captured[1].Decision.IsApproved);
    }

    [Fact]
    public async Task UIBoundApprovalService_DelegatesToViewModel()
    {
        var vm = new ToolApprovalViewModel { IsReadOnly = true };
        IApprovalService service = new UIBoundApprovalService(vm);
        var request = NewRequest("write_file");

        var decision = await service.RequestApprovalAsync(request, CancellationToken.None);

        Assert.False(decision.IsApproved);
        Assert.Equal("只读模式已开启。", decision.Reason);
    }

    // ---- 1.0.1: cron-engine unattended-approval-timeout ----

    [Fact]
    public void RejectPendingIfAny_NoPending_IsNoOp()
    {
        var vm = new ToolApprovalViewModel();

        // Should not throw, should not flip
        // HasPendingApproval, should not fire
        // RequestResolved (there's nothing to
        // resolve).
        var resolvedFired = false;
        vm.RequestResolved += (_, _) => resolvedFired = true;

        vm.RejectPendingIfAny("无人值守 timeout");

        Assert.False(vm.HasPendingApproval);
        Assert.False(resolvedFired);
    }

    [Fact]
    public async Task RejectPendingIfAny_Pending_RejectsWithSuppliedReason()
    {
        var vm = new ToolApprovalViewModel();
        var request = NewRequest("write_file");

        // Kick off the presentation. The VM is
        // synchronous in the headless test host
        // (no Avalonia dispatcher), so by the time
        // PresentRequestAsync returns the surface
        // is already populated and HasPendingApproval
        // is true.
        var presented = vm.PresentRequestAsync(request, CancellationToken.None);

        Assert.True(vm.HasPendingApproval);

        // Now reject from the unattended-timeout path.
        vm.RejectPendingIfAny("auto-rejected (无人值守 timeout)");

        // HasPendingApproval flips off, the original
        // awaiter resolves with a Reject decision
        // whose reason matches the timeout message.
        Assert.False(vm.HasPendingApproval);
        var decision = await presented;
        Assert.False(decision.IsApproved);
        Assert.Contains("无人值守", decision.Reason ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectPendingIfAny_AfterUserRejects_DoesNotDoubleResolve()
    {
        // Defensive contract: if the user beats the
        // timeout to the punch and clicks Reject
        // themselves, the pending request is already
        // resolved. The cron engine's later
        // RejectPendingIfAny call is a no-op (the VM
        // has nothing pending). The original
        // awaiter's decision still reflects the
        // user's click, not the timeout.
        var vm = new ToolApprovalViewModel();
        var request = NewRequest("write_file");
        var presented = vm.PresentRequestAsync(request, CancellationToken.None);

        // User rejects first.
        vm.RejectCommand.Execute(null);
        var userDecision = await presented;
        Assert.False(userDecision.IsApproved);

        // Cron-engine timeout fires after the user
        // already resolved. No-op; the previous
        // decision stays the user's.
        vm.RejectPendingIfAny("auto-rejected (无人值守 timeout)");

        Assert.False(vm.HasPendingApproval);
    }

    // ---- 1.0.1: modal countdown (cron-engine "auto-reject in Ns" hint) ----

    [Fact]
    public void StartUnattendedCountdown_SetsSecondsRemainingAndActiveFlag()
    {
        // 30s is the cron engine's default
        // (UnattendedApprovalTimeout in
        // AgentHostScheduledTaskExecutor). The
        // view-model should round 30s up to 30
        // (Math.Ceiling, so any sub-second
        // remainder counts as a full second).
        var vm = new ToolApprovalViewModel();
        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(30));

        Assert.True(vm.IsUnattendedCountdownActive);
        Assert.Equal(30, vm.UnattendedSecondsRemaining);
    }

    [Fact]
    public void StartUnattendedCountdown_RoundsUpFractionalSeconds()
    {
        // 1.2s — the ceil rule keeps a slow
        // dispatcher tick (which can land 50-100ms
        // late on a busy box) from missing the
        // timeout by zeroing one second too early.
        var vm = new ToolApprovalViewModel();
        vm.StartUnattendedCountdown(TimeSpan.FromMilliseconds(1200));

        Assert.Equal(2, vm.UnattendedSecondsRemaining);
    }

    [Fact]
    public async Task StartUnattendedCountdown_AfterResolve_StopsTicking()
    {
        // Approve before the timer expires —
        // the countdown must be torn down so the
        // modal's "auto-reject in Ns" line hides
        // itself and the dispatcher doesn't keep
        // running a stale timer that would fire
        // a phantom reject on a later request.
        var vm = new ToolApprovalViewModel();
        var request = NewRequest("write_file");
        var presented = vm.PresentRequestAsync(request, CancellationToken.None);

        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsUnattendedCountdownActive);

        vm.ApproveCommand.Execute(null);
        await presented;

        Assert.False(vm.IsUnattendedCountdownActive);
        Assert.Equal(0, vm.UnattendedSecondsRemaining);
    }

    [Fact]
    public async Task StartUnattendedCountdown_Reject_StopsCountdown()
    {
        var vm = new ToolApprovalViewModel();
        var presented = vm.PresentRequestAsync(NewRequest("write_file"), CancellationToken.None);
        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsUnattendedCountdownActive);

        vm.RejectCommand.Execute(null);
        await presented;

        Assert.False(vm.IsUnattendedCountdownActive);
        Assert.Equal(0, vm.UnattendedSecondsRemaining);
    }

    [Fact]
    public async Task StartUnattendedCountdown_TimerFires_RejectsAndFiresEvent()
    {
        // The DispatcherTimer doesn't actually
        // tick in a headless test host (no live
        // Avalonia Application.Current to drive
        // the dispatcher loop), so we drive the
        // tick handler directly. The contract
        // we care about: when OnUnattendedTick
        // decrements past zero, the view-model
        // (a) fires UnattendedTimeoutFired so
        // the cron engine can mark the run as
        // Failed, and (b) calls RejectPendingIfAny
        // with the standard timeout reason so
        // the awaiter sees a clear "auto-
        // rejected (无人值守 timeout)" decision.
        var vm = new ToolApprovalViewModel();
        var request = NewRequest("write_file");
        var presented = vm.PresentRequestAsync(request, CancellationToken.None);

        var eventFired = false;
        vm.UnattendedTimeoutFired += (_, _) => eventFired = true;

        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(1));
        Assert.Equal(1, vm.UnattendedSecondsRemaining);

        // Simulate the first 1Hz tick — VM
        // decrements 1 → 0, hits the timeout
        // branch, fires the event, rejects.
        InvokeTick(vm);

        Assert.True(eventFired);
        Assert.False(vm.IsUnattendedCountdownActive);
        var decision = await presented;
        Assert.False(decision.IsApproved);
        Assert.Contains("无人值守", decision.Reason ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartUnattendedCountdown_IsIdempotent()
    {
        // Re-arming a live countdown resets
        // the seconds-remaining back to the
        // new timeout. Used when the user
        // clears a prior pending and a new
        // one comes in during the same run —
        // we don't want the second approval
        // to inherit a stale, half-expired
        // deadline.
        var vm = new ToolApprovalViewModel();
        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(5));
        Assert.Equal(5, vm.UnattendedSecondsRemaining);

        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(30));
        Assert.Equal(30, vm.UnattendedSecondsRemaining);
        Assert.True(vm.IsUnattendedCountdownActive);
    }

    [Fact]
    public async Task UIBoundApprovalService_ForwardsCountdownAndEvent()
    {
        // The cron engine talks to IApprovalService
        // — the service must arm the countdown on
        // the underlying view-model and forward
        // UnattendedTimeoutFired so the engine
        // never has to know about the Avalonia
        // view-model directly.
        var vm = new ToolApprovalViewModel();
        IApprovalService service = new UIBoundApprovalService(vm);

        // Headless test host: PresentRequestAsync
        // runs the UI-marshal inline, so the
        // surface is set up before the call
        // returns. Service.StartUnattendedCountdown
        // therefore has a pending request to arm.
        var presented = service.RequestApprovalAsync(
            NewRequest("write_file"), CancellationToken.None);

        service.StartUnattendedCountdown(TimeSpan.FromSeconds(15));
        Assert.Equal(15, vm.UnattendedSecondsRemaining);
        Assert.True(vm.IsUnattendedCountdownActive);

        // Drive a tick on the VM directly. The
        // service's UnattendedTimeoutFired event
        // should relay the VM's signal.
        var serviceEventFired = false;
        service.UnattendedTimeoutFired += (_, _) => serviceEventFired = true;

        vm.StartUnattendedCountdown(TimeSpan.FromSeconds(1));
        InvokeTick(vm);

        Assert.True(serviceEventFired);
        var decision = await presented;
        Assert.False(decision.IsApproved);
    }

    private static void InvokeTick(ToolApprovalViewModel vm)
    {
        // The Timer.Tick handler is private; drive
        // it through reflection so we can verify the
        // countdown-decrement / auto-reject branch
        // without owning a live Avalonia dispatcher.
        var method = typeof(ToolApprovalViewModel)
            .GetMethod("OnUnattendedTick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(vm, new object?[] { null, EventArgs.Empty });
    }

    private static ToolApprovalRequest NewRequest(string toolName)
    {
        return new ToolApprovalRequest
        {
            ToolCall = new ChatToolCall
            {
                Id = "call-1",
                Name = toolName,
                ArgumentsJson = "{}"
            },
            Preview = new AgentToolPreview
            {
                ToolName = toolName,
                Summary = "summary",
                PreviewText = "preview",
                DiffText = ""
            }
        };
    }
}
