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
