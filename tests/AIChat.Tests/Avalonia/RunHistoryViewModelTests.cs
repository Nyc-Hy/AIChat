using AIChat.Abstractions.Configuration;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Avalonia;

// T-RH layer: RunHistoryViewModel v1 paths (plan §7.3 Wave 2.11)。
// v1 model 把 sessions 拆出 project,sessions 走 external store;
// sidebar 持有 CurrentProjectSessions,RunHistoryViewModel.Refresh
// 从它读。Wave 2 之前这些 test 走 v0 project.Conversations,迁移后
// 走 v1 sessions 路径,关键 surface 没变:
//   1. 当前 project 的 sessions 的 AgentRuns 全部出现
//   2. 状态过滤按 AgentRun.Status 匹配
//   3. retry / continue 注入 action 在命令触发时调
public sealed class RunHistoryViewModelTests : IDisposable
{
    private readonly string _projectPath = Path.Combine(
        Path.GetTempPath(),
        "AIChatRunHistoryTests",
        Guid.NewGuid().ToString("N"));

    public RunHistoryViewModelTests() => Directory.CreateDirectory(_projectPath);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectPath, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    // Helper: build a WorkspaceProject with a single folder rooted at _projectPath,
    // and the given ChatSession list (Project-typed) wired to the workspace.
    private static (WorkspaceProject, List<ChatSession>) BuildWorkspaceWithSessions(
        string workspaceId,
        IReadOnlyList<AgentRun> runs)
    {
        var folderId = Guid.NewGuid().ToString("N");
        var workspace = new WorkspaceProject
        {
            Id = workspaceId,
            Name = "test",
            Folders = [new WorkspaceFolder { Id = folderId, Path = "/tmp/repo" }],
            PrimaryFolderId = folderId,
        };
        var session = new Project
        {
            WorkspaceId = workspaceId,
            Id = "session-1",
            Title = "test session",
            AgentRuns = runs.ToList(),
        };
        return (workspace, [session]);
    }

    [Fact]
    public void Refresh_LoadsRunsFromCurrentProjectSessions()
    {
        // 核心场景:sidebar 持有 sessions,RunHistoryViewModel.Refresh
        // 把 sessions 里的 AgentRuns 全部读出来按 StartedAt desc 排
        var repository = new InMemoryAppRepository();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, holder);

        var runs = new List<AgentRun>
        {
            new() { Id = "r1", StartedAt = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero) },
            new() { Id = "r2", StartedAt = new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.Zero) },
            new() { Id = "r3", StartedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero) },
        };
        var (workspace, sessions) = BuildWorkspaceWithSessions("ws-1", runs);
        sidebar.Refresh([workspace]);
        sidebar.UpdateCurrentProjectSessions(sessions);

        var vm = new RunHistoryViewModel(sidebar, _ => { }, _ => { });
        vm.Refresh();

        Assert.Equal(3, vm.Runs.Count);
        // Order: startedAt desc
        Assert.Equal("r2", vm.Runs[0].Run.Id);
        Assert.Equal("r1", vm.Runs[1].Run.Id);
        Assert.Equal("r3", vm.Runs[2].Run.Id);
    }

    [Fact]
    public void Refresh_FiltersRunsBySelectedStatus()
    {
        // 状态过滤:SelectedStatusFilter != "全部" 时只显示匹配 status 的 runs
        var repository = new InMemoryAppRepository();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, holder);

        var runs = new List<AgentRun>
        {
            new() { Id = "r1", Status = AgentRunStatus.Completed },
            new() { Id = "r2", Status = AgentRunStatus.Failed },
            new() { Id = "r3", Status = AgentRunStatus.Completed },
            new() { Id = "r4", Status = AgentRunStatus.Cancelled },
        };
        var (workspace, sessions) = BuildWorkspaceWithSessions("ws-1", runs);
        sidebar.Refresh([workspace]);
        sidebar.UpdateCurrentProjectSessions(sessions);

        var vm = new RunHistoryViewModel(sidebar, _ => { }, _ => { });
        vm.Refresh();

        // 默认 "全部" — 4 个全显示
        Assert.Equal(4, vm.Runs.Count);

        // 切到 "完成" — 2 个
        vm.SelectedStatusFilter = "完成";
        Assert.Equal(2, vm.Runs.Count);
        Assert.All(vm.Runs, run => Assert.Equal(AgentRunStatus.Completed, run.Run.Status));

        // 切到 "失败" — 1 个
        vm.SelectedStatusFilter = "失败";
        Assert.Single(vm.Runs);
        Assert.Equal("r2", vm.Runs[0].Run.Id);
    }

    [Fact]
    public void RetryCommand_WithSelectedRun_InvokesInjectedRetryAction()
    {
        // retry / continue 注入 action 在命令触发时被调;
        // CanActOnSelected 要求 selected.Run.Status != Running
        var repository = new InMemoryAppRepository();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, holder);

        var runs = new List<AgentRun>
        {
            new() { Id = "r1", Status = AgentRunStatus.Failed, Goal = "test" }
        };
        var (workspace, sessions) = BuildWorkspaceWithSessions("ws-1", runs);
        sidebar.Refresh([workspace]);
        sidebar.UpdateCurrentProjectSessions(sessions);

        RunHistoryItemViewModel? retriedItem = null;
        RunHistoryItemViewModel? continuedItem = null;
        var vm = new RunHistoryViewModel(
            sidebar,
            item => retriedItem = item,
            item => continuedItem = item);
        vm.Refresh();

        // 选第一个 run
        vm.SelectedRun = vm.Runs[0];

        // 触发 retry
        vm.RetrySelectedCommand.Execute(null);
        Assert.NotNull(retriedItem);
        Assert.Equal("r1", retriedItem!.Run.Id);
        Assert.Null(continuedItem);

        // 触发 continue(应在 retry 之后清掉 retriedItem 单独 hold)
        retriedItem = null;
        vm.ContinueSelectedCommand.Execute(null);
        Assert.NotNull(continuedItem);
        Assert.Equal("r1", continuedItem!.Run.Id);
        Assert.Null(retriedItem);
    }

    // 1.0.1: MainWindowViewModel.
    // CopyRunGoalToComposer drops the
    // historical Goal into the
    // composer's DraftPrompt, closes
    // the RunHistory modal, and
    // raises FocusComposerRequested
    // so MainWindow.xaml.cs puts
    // the caret in the prompt
    // input. The 3 lines after the
    // agent host set are the same
    // pattern NewStandaloneConversationAsync
    // uses (close source modal +
    // raise focus event).
    [Fact]
    public void CopyRunGoalToComposer_SetsDraftPromptAndClosesModalAndRaisesFocus()
    {
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        viewModel.IsRunHistoryOpen = true;

        var focusCount = 0;
        viewModel.FocusComposerRequested += (_, _) => focusCount++;

        viewModel.CopyRunGoalToComposer("  refactor auth middleware  ");

        Assert.Equal("  refactor auth middleware  ", viewModel.AgentHost.DraftPrompt);
        Assert.False(viewModel.IsRunHistoryOpen);
        Assert.Equal(1, focusCount);
    }

    [Fact]
    public void CopyRunGoalToComposer_NullOrWhitespace_IsNoOp()
    {
        // Defensive: the XAML's Tag binding
        // is a string, but a refactor that
        // swapped to a nullable source
        // would land null here. The
        // method must not crash the
        // RunHistory view's click handler.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        viewModel.IsRunHistoryOpen = true;

        viewModel.CopyRunGoalToComposer(null);
        viewModel.CopyRunGoalToComposer("");
        viewModel.CopyRunGoalToComposer("   ");

        Assert.True(viewModel.IsRunHistoryOpen);
    }
}
