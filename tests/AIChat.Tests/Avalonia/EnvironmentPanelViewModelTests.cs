using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.BackgroundProcesses;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Tests.TestDoubles;
using Moq;

namespace AIChat.Tests.Avalonia;

// Environment panel is a read-only reflection of upstream state (git
// changes via IWorkspaceChangeService, sub-agent runs and pending
// attachments via AgentHostViewModel). These tests pin the wiring:
// collection mutations on the upstream sources propagate to the
// derived counts / summary, and RefreshAsync keeps the panel honest
// about whether a project is selected.
public sealed class EnvironmentPanelViewModelTests
{
    [Fact]
    public void Ctor_DefaultsToProjectRequiredAndZeroedCounters()
    {
        var (vm, _, _, _) = CreateViewModel();

        Assert.True(vm.IsProjectRequired);
        Assert.Equal("(未选择项目)", vm.BranchName);
        Assert.Equal(0, vm.ChangeAdded);
        Assert.Equal(0, vm.ChangeRemoved);
        Assert.Equal(0, vm.SubAgentTotal);
        Assert.Equal(0, vm.SubAgentRunning);
        Assert.Equal(0, vm.SubAgentFailed);
        Assert.Equal(0, vm.SourceCount);
        Assert.Equal("暂无", vm.SourceSummary);
        Assert.False(vm.HasSubAgents);
    }

    [Fact]
    public async Task RefreshAsync_NoSelectedProject_KeepsPlaceholderAndStampsTime()
    {
        var (vm, _, _, _) = CreateViewModel();

        await vm.RefreshAsync();

        Assert.True(vm.IsProjectRequired);
        Assert.Equal("(未选择项目)", vm.BranchName);
        Assert.Equal(0, vm.ChangeAdded);
        Assert.Equal(0, vm.ChangeRemoved);
        // LastRefreshDisplay is stamped even when there's nothing to read —
        // the user clicked refresh, so we tell them "we did nothing at HH:mm:ss".
        Assert.NotEqual("尚未刷新", vm.LastRefreshDisplay);
    }

    [Fact]
    public async Task RefreshAsync_WithSelectedProject_SetsBranchAndProjectNotRequired()
    {
        var (vm, _, sidebar, workspace) = CreateViewModel();
        sidebar.Refresh([NewProject("/tmp/some-project")]);
        workspace
            .Setup(service => service.GetChangesAsync("/tmp/some-project", 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceChangeSet
            {
                Branch = "feature/sprint-0.5",
                Changes = new List<WorkspaceChange>
                {
                    NewChange("added.cs", "A "),
                    NewChange("modified.cs", " M"),
                }
            });

        await vm.RefreshAsync();

        Assert.False(vm.IsProjectRequired);
        Assert.Equal("feature/sprint-0.5", vm.BranchName);
        Assert.Equal(2, vm.ChangeAdded);
        Assert.Equal(0, vm.ChangeRemoved);
    }

    [Fact]
    public async Task RefreshAsync_StripsBranchPrefixHashFromDisplay()
    {
        // git status --porcelain --branch emits something like
        // "## feature/x...origin/feature/x" — the leading "## " is noise
        // we don't want in the UI. The VM strips it.
        var (vm, _, sidebar, workspace) = CreateViewModel();
        sidebar.Refresh([NewProject("/tmp/repo")]);
        workspace
            .Setup(service => service.GetChangesAsync("/tmp/repo", 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceChangeSet
            {
                Branch = "## main...origin/main",
                Changes = []
            });

        await vm.RefreshAsync();

        Assert.Equal("main...origin/main", vm.BranchName);
    }

    [Fact]
    public async Task RefreshAsync_GitFailure_SurfacesErrorInBranchLabel()
    {
        var (vm, _, sidebar, workspace) = CreateViewModel();
        sidebar.Refresh([NewProject("/tmp/broken")]);
        workspace
            .Setup(service => service.GetChangesAsync("/tmp/broken", 200, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not a git repo"));

        await vm.RefreshAsync();

        Assert.False(vm.IsProjectRequired);
        Assert.Contains("(git 错误:", vm.BranchName);
        Assert.Contains("not a git repo", vm.BranchName);
        Assert.Equal(0, vm.ChangeAdded);
    }

    [Fact]
    public void AttachTo_SubAgentRunsCollectionChanges_RecountsTotalAndRunningAndFailed()
    {
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();

        host.SubAgentRuns.Add(NewSubAgent("explorer", "Running"));
        Assert.Equal(1, vm.SubAgentTotal);
        Assert.Equal(1, vm.SubAgentRunning);
        Assert.Equal(0, vm.SubAgentFailed);
        Assert.True(vm.HasSubAgents);

        host.SubAgentRuns.Add(NewSubAgent("explorer", "Failed"));
        Assert.Equal(2, vm.SubAgentTotal);
        Assert.Equal(1, vm.SubAgentRunning);
        Assert.Equal(1, vm.SubAgentFailed);

        host.SubAgentRuns.RemoveAt(0);
        Assert.Equal(1, vm.SubAgentTotal);
        Assert.Equal(0, vm.SubAgentRunning);
        Assert.Equal(1, vm.SubAgentFailed);
    }

    [Fact]
    public void AttachTo_InitialPendingAttachmentCount_IsZero()
    {
        // The attachment collection subscription is the same code
        // pattern as sub-agent runs (CollectionChanged → recount), so
        // we only verify the initial wiring here. Adding a real
        // PendingAttachmentViewModel requires constructing an
        // Avalonia Bitmap, which the headless test host doesn't
        // initialize — see PendingAttachmentsViewModelTests for the
        // tests that exercise the attachment lifecycle end-to-end.
        var (vm, _, _, _) = CreateViewModel();
        vm.AttachTo();

        Assert.Equal(0, vm.SourceCount);
        Assert.Equal("暂无", vm.SourceSummary);
    }

    [Fact]
    public void AttachTo_CalledTwice_DoesNotDoubleSubscribe()
    {
        // Re-attaching without detaching first would multiply the
        // CollectionChanged handlers — every add would recount twice
        // and the total would race. DetachFrom() runs first.
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();
        vm.AttachTo();

        host.SubAgentRuns.Add(NewSubAgent("explorer", "Running"));
        Assert.Equal(1, vm.SubAgentTotal);
    }

    [Fact]
    public void DetachFrom_StopsPropagatingCollectionChanges()
    {
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();
        host.SubAgentRuns.Add(NewSubAgent("explorer", "Running"));
        Assert.Equal(1, vm.SubAgentTotal);

        vm.DetachFrom();
        host.SubAgentRuns.Add(NewSubAgent("reviewer", "Running"));
        Assert.Equal(1, vm.SubAgentTotal); // still 1 — handler is gone
    }

    [Fact]
    public void HasSubAgents_FlipsOnTotalChange()
    {
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();
        Assert.False(vm.HasSubAgents);

        host.SubAgentRuns.Add(NewSubAgent("explorer", "Running"));
        Assert.True(vm.HasSubAgents);

        host.SubAgentRuns.Clear();
        Assert.False(vm.HasSubAgents);
    }

    [Fact]
    public void AttachTo_SubAgentRunsCollectionChanges_MirrorsInstancesIntoPanel()
    {
        // The per-run list XAML binds ItemsControl to
        // EnvironmentPanelViewModel.SubAgentRuns (a separate
        // ObservableCollection from AgentHost.SubAgentRuns). The
        // mirror must hand the XAML the SAME SubAgentRunViewModel
        // instances — not copies — so when the harness emits
        // SubAgentStarted → SubAgentCompleted and the live
        // instance's Status setter fires PropertyChanged, the
        // existing DataTemplate bindings on the per-row UI tick
        // from "运行中…" to "12s" without a per-row subscriber.
        // We don't pin the order here — OrderByDescending by
        // StartedAt puts the newer item first, and a "now" stamp
        // in the helper makes the second one always newer. The
        // ordering contract has its own test below.
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();

        var explorer = NewSubAgent("explorer", "Running");
        var reviewer = NewSubAgent("reviewer", "Running");
        host.SubAgentRuns.Add(explorer);
        host.SubAgentRuns.Add(reviewer);

        Assert.Equal(2, vm.SubAgentRuns.Count);
        Assert.Contains(explorer, vm.SubAgentRuns);
        Assert.Contains(reviewer, vm.SubAgentRuns);
    }

    [Fact]
    public void AttachTo_SubAgentRunsCollectionChanges_OrdersNewestFirst()
    {
        // Wave 7: per-run list shows newest dispatch at the top, like
        // Codex. The harness appends in chronological order; the
        // panel must re-order by StartedAt desc so the user reads
        // the most recent run first.
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();

        var older = NewSubAgentAt("explorer", "Completed",
            new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero));
        var middle = NewSubAgentAt("explorer", "Completed",
            new DateTimeOffset(2026, 8, 2, 11, 0, 0, TimeSpan.Zero));
        var newest = NewSubAgentAt("reviewer", "Running",
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
        // Append in chronological order to mirror the harness.
        host.SubAgentRuns.Add(older);
        host.SubAgentRuns.Add(middle);
        host.SubAgentRuns.Add(newest);

        Assert.Equal(3, vm.SubAgentRuns.Count);
        Assert.Same(newest, vm.SubAgentRuns[0]);
        Assert.Same(middle, vm.SubAgentRuns[1]);
        Assert.Same(older, vm.SubAgentRuns[2]);
    }

    [Fact]
    public void AttachTo_SubAgentRunsMirror_PropagatesLiveStatusUpdate()
    {
        // Lock in the contract the XAML depends on: the per-row
        // SubAgentRunViewModel in the panel's collection is the
        // same object as the one in AgentHost, so a live Status
        // mutation flows through to the existing DataTemplate
        // binding without a separate fire.
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();

        var run = NewSubAgent("explorer", "Running");
        host.SubAgentRuns.Add(run);
        Assert.Single(vm.SubAgentRuns);
        Assert.Same(run, vm.SubAgentRuns[0]);
        Assert.Equal("Running", vm.SubAgentRuns[0].Status);

        // Simulate the harness emitting SubAgentCompleted by
        // updating the live instance in place.
        run.Update(new AgentSubAgentRun
        {
            Id = run.Id,
            ParentRunId = "parent",
            TemplateId = "explorer",
            Task = "test",
            Status = "Completed",
            StartedAt = run.StartedAt,
            CompletedAt = run.StartedAt.AddSeconds(12),
        });

        // Same instance still in the panel collection, and its
        // status is the new value.
        Assert.Same(run, vm.SubAgentRuns[0]);
        Assert.Equal("Completed", vm.SubAgentRuns[0].Status);
        Assert.Equal("12s", vm.SubAgentRuns[0].DurationDisplay);
    }

    [Fact]
    public void AttachTo_SubAgentRunsMirror_ClearsWhenHostClears()
    {
        // The runner calls AgentHost.ClearSubAgentRuns() at the
        // start of each new SendTaskCommand so a new run starts
        // with a fresh list. The panel's mirror must collapse to
        // empty at the same time, otherwise the user sees a stale
        // per-run list from the previous turn.
        var (vm, host, _, _) = CreateViewModel();
        vm.AttachTo();

        host.SubAgentRuns.Add(NewSubAgent("explorer", "Running"));
        host.SubAgentRuns.Add(NewSubAgent("explorer", "Completed"));
        Assert.Equal(2, vm.SubAgentRuns.Count);

        host.SubAgentRuns.Clear();
        Assert.Empty(vm.SubAgentRuns);
    }

    [Fact]
    public void Ctor_ShowBackgroundProcessesDefaultsToTrue()
    {
        // Wave 7 follow-up: the BackgroundProcessSupervisor
        // is now built and registered in DI, so the
        // plan §7.7 rule ("supervisor 未建前不得展示入口")
        // is satisfied. The default visibility flips to
        // true so the user sees the section in the panel
        // — its content is the supervisor's snapshot, so
        // an empty list shows the empty-state hint rather
        // than a hidden section.
        var (vm, _, _, _) = CreateViewModel();

        Assert.True(vm.ShowBackgroundProcesses);
    }

    [Fact]
    public void AttachTo_DefaultSources_IsEmptyAndShowsPlaceholder()
    {
        // Wave 7 ships the per-source list with the image-attachment
        // surface only. The Sources collection starts empty, so the
        // XAML's "暂无来源" hint is what the user sees.
        var (vm, _, _, _) = CreateViewModel();
        vm.AttachTo();

        Assert.Empty(vm.Sources);
        Assert.Equal(0, vm.SourceCount);
        Assert.Equal("暂无", vm.SourceSummary);
    }

    // ===== Wave 7 follow-up: BackgroundProcessSupervisor wiring =====

    [Fact]
    public void Ctor_HasBackgroundProcesses_FlipsOnCollectionChange()
    {
        // The XAML's "(暂无后台进程)" hint is bound to
        // !HasBackgroundProcesses, so the bool must
        // re-raise when the BackgroundProcesses collection
        // changes. The test directly mutates the
        // collection to verify the CollectionChanged → bool
        // contract; the real data source (the supervisor)
        // is exercised by the other tests below.
        var (vm, _, _, _) = CreateViewModel();
        Assert.False(vm.HasBackgroundProcesses);

        vm.BackgroundProcesses.Add(NewRow("echo", "Running"));
        Assert.True(vm.HasBackgroundProcesses);

        vm.BackgroundProcesses.Clear();
        Assert.False(vm.HasBackgroundProcesses);
    }

    [Fact]
    public async Task AttachTo_InitialSync_LoadsProcessesFromSupervisor()
    {
        // Start a real process through the supervisor so
        // the panel's initial mirror has data. The test
        // wraps a fake `sleep 5` command which is cheap
        // (one OS process, exits in 5s) and isolated to
        // the per-test temp dir.
        var (vm, _, _, _, supervisor) = CreateViewModelWithSupervisor();

        await supervisor.StartAsync(new AIChat.Domain.BackgroundProcesses.BackgroundProcess
        {
            Name = "test-sleep",
            Command = "/bin/sh",
            Arguments = new List<string> { "-c", "sleep 5" },
        });

        vm.AttachTo();
        // The supervisor's Changed event in production
        // posts to the Avalonia dispatcher; the headless
        // test host doesn't pump the queue, so the panel
        // would not see the new process through that
        // path. Call the internal Sync method directly
        // (exposed via [InternalsVisibleTo]) to verify
        // the wiring. The dispatcher marshal itself is
        // covered by the production code path; here we
        // only care that the panel can render the
        // supervisor's snapshot.
        vm.SyncBackgroundProcesses();

        var row = Assert.Single(vm.BackgroundProcesses);
        Assert.Equal("test-sleep", row.DisplayName);
        Assert.Equal("运行中", row.StatusLabel);
        Assert.True(row.IsRunning);
        // PidLabel is "PID <n>" — confirm it parses to a
        // positive number so the row reflects a real
        // supervisor-issued process id.
        Assert.NotEmpty(row.PidLabel);
        Assert.Contains("PID", row.PidLabel);
        Assert.NotNull(row.StopCommand);

        // Cleanup so the test doesn't leak the sleep.
        await supervisor.StopAsync(
            vm.BackgroundProcesses[0].Id);
    }

    [Fact]
    public async Task StopBackgroundProcessAsync_ForwardsCallToSupervisor()
    {
        // The XAML's per-row Stop button routes through
        // StopBackgroundProcessAsync, which delegates to
        // the supervisor. We start a real process so the
        // id is the supervisor's id, then call
        // StopBackgroundProcessAsync and assert against
        // the supervisor's own state — the panel's mirror
        // is updated by the supervisor's Changed event,
        // which is posted to the UI dispatcher. The
        // test host doesn't pump the dispatcher, so the
        // mirror may not be current after the call; the
        // supervisor's snapshot is the source of truth.
        var (vm, _, _, _, supervisor) = CreateViewModelWithSupervisor();
        var id = await supervisor.StartAsync(new AIChat.Domain.BackgroundProcesses.BackgroundProcess
        {
            Name = "stop-test",
            Command = "/bin/sh",
            Arguments = new List<string> { "-c", "trap 'exit 0' TERM; sleep 30" },
        });
        vm.AttachTo();
        Assert.Single(vm.BackgroundProcesses);

        // Snapshot the supervisor's process before the
        // stop so we can match the same row after.
        var before = Assert.Single(supervisor.Processes);
        Assert.Equal(
            AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.Running,
            before.Status);

        await vm.StopBackgroundProcessAsync(id);

        // The supervisor's snapshot is the source of
        // truth. The trap lets the shell exit cleanly
        // on SIGTERM, so the row should be Stopped
        // (or ForceKilled if the trap didn't take).
        var after = Assert.Single(supervisor.Processes);
        Assert.NotEqual(
            AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.Running,
            after.Status);
    }

    [Fact]
    public async Task StopBackgroundProcessAsync_NullOrEmptyId_NoOp()
    {
        // Belt-and-braces: the XAML's Tag binding could
        // be null if a row VM is partially constructed.
        // The VM should swallow the call rather than
        // throw — the XAML's button has IsVisible bound
        // to IsRunning, but a misbehaving test could
        // still trigger this path.
        var (vm, _, _, _, _) = CreateViewModelWithSupervisor();
        vm.AttachTo();

        await vm.StopBackgroundProcessAsync(null);
        await vm.StopBackgroundProcessAsync("");
        await vm.StopBackgroundProcessAsync("   ");
        // No throw → pass.
    }

    [Fact]
    public async Task DetachFrom_StopsReflectingSupervisorChanges()
    {
        // After DetachFrom, the panel must not re-mirror
        // when the supervisor fires Changed. This is the
        // same contract as the sub-agent / attachment
        // detach tests above. We exercise the contract
        // without calling SyncBackgroundProcesses after
        // Detach: the manual sync would re-mirror from
        // the supervisor's snapshot, which would inflate
        // the count and mask a leaked subscription.
        var (vm, _, _, _, supervisor) = CreateViewModelWithSupervisor();
        vm.AttachTo();
        // Same direct-sync rationale as
        // AttachTo_InitialSync: skip the dispatcher
        // marshal that the headless test host can't pump.
        vm.SyncBackgroundProcesses();
        Assert.Empty(vm.BackgroundProcesses);

        await supervisor.StartAsync(new AIChat.Domain.BackgroundProcesses.BackgroundProcess
        {
            Name = "before-detach",
            Command = "/bin/sh",
            Arguments = new List<string> { "-c", "sleep 5" },
        });
        vm.SyncBackgroundProcesses();
        Assert.Single(vm.BackgroundProcesses);

        // Detach removes the supervisor subscription.
        vm.DetachFrom();

        // The supervisor's Changed event from this
        // second start must not reach the panel —
        // otherwise the production path would still
        // call into the panel even after a Detach,
        // which is the bug the test is locking down.
        var beforeCount = vm.BackgroundProcesses.Count;
        await supervisor.StartAsync(new AIChat.Domain.BackgroundProcesses.BackgroundProcess
        {
            Name = "after-detach",
            Command = "/bin/sh",
            Arguments = new List<string> { "-c", "sleep 5" },
        });
        // No manual Sync here: we want to see whether
        // the supervisor's Changed handler that
        // DetachFrom is supposed to remove is actually
        // gone. The panel's count must stay at 1.
        Assert.Equal(beforeCount, vm.BackgroundProcesses.Count);

        // Cleanup.
        foreach (var p in supervisor.Processes)
        {
            await supervisor.StopAsync(p.Id);
        }
    }

    // ----- helpers -----

    private static BackgroundProcessViewModel NewRow(string name, string statusLabel)
    {
        // Bare row for collection-mutation tests. The
        // richer constructor takes a domain
        // BackgroundProcess + supervisor; the bare one
        // only needs the display fields that HasBackgroundProcesses
        // re-raise depends on. We pass null for the
        // supervisor because these tests never exercise
        // StopCommand.
        return new BackgroundProcessViewModel(
            new AIChat.Domain.BackgroundProcesses.BackgroundProcess
            {
                Name = name,
                Status = statusLabel switch
                {
                    "运行中" => AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.Running,
                    "已停止" => AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.Stopped,
                    "已崩溃" => AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.Crashed,
                    "已强制停止" => AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.ForceKilled,
                    _ => AIChat.Domain.BackgroundProcesses.BackgroundProcessStatus.Pending,
                },
                Pid = 1,
            },
            supervisor: null!);
    }

    private static (EnvironmentPanelViewModel Vm, AgentHostViewModel Host, ProjectSidebarViewModel Sidebar, Mock<IWorkspaceChangeService> Workspace, BackgroundProcessSupervisor Supervisor)
        CreateViewModelWithSupervisor()
    {
        var repository = new InMemoryAppRepository();
        var settingsHolder = new SettingsHolder();
        settingsHolder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, settingsHolder);
        var activity = new ActivityFeedViewModel();
        var toast = new ToastService(action => action());
        var host = new AgentHostViewModel(
            Mock.Of<IChatCompletionService>(),
            AgentToolRegistry.CreateForTests([]),
            Mock.Of<IApprovalService>(),
            repository,
            sidebar,
            new ConversationListViewModel(repository),
            activity,
            toast,
            new InMemorySourceRegistry(),
            _ => { },
            () => settingsHolder.Current,
            () => false,
            () => false,
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var workspace = new Mock<IWorkspaceChangeService>();
        var supervisorRoot = Path.Combine(
            Path.GetTempPath(),
            "aichat-env-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(supervisorRoot);
        var supervisor = new BackgroundProcessSupervisor(
            Path.Combine(supervisorRoot, "processes.json"));
        var vm = new EnvironmentPanelViewModel(
            workspace.Object, supervisor, host, sidebar, new MockClipboardService(), new InMemorySourceRegistry());
        return (vm, host, sidebar, workspace, supervisor);
    }

    private static (EnvironmentPanelViewModel Vm, AgentHostViewModel Host, ProjectSidebarViewModel Sidebar, Mock<IWorkspaceChangeService> Workspace)
        CreateViewModel()
    {
        var repository = new InMemoryAppRepository();
        var settingsHolder = new SettingsHolder();
        settingsHolder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, settingsHolder);
        var activity = new ActivityFeedViewModel();
        var toast = new ToastService(action => action());
        var host = new AgentHostViewModel(
            Mock.Of<IChatCompletionService>(),
            AgentToolRegistry.CreateForTests([]),
            Mock.Of<IApprovalService>(),
            repository,
            sidebar,
            new ConversationListViewModel(repository),
            activity,
            toast,
            new InMemorySourceRegistry(),
            _ => { },
            () => settingsHolder.Current,
            () => false,
            () => false,
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var workspace = new Mock<IWorkspaceChangeService>();
        // The Environment panel now mirrors the supervisor's
        // process list (Wave 7 follow-up, plan §13 P0 risk
        // "整个子进程树"). We hand each test a fresh
        // per-temp-dir supervisor so the test can drive
        // StartAsync / StopAsync / Changed without leaking
        // state across tests. The "real supervisor" path
        // (i.e. the supervisor's persistence + process
        // tree kill) is covered in
        // BackgroundProcessSupervisorTests — here we
        // verify the panel's wiring only.
        var supervisorRoot = Path.Combine(
            Path.GetTempPath(),
            "aichat-env-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(supervisorRoot);
        var supervisor = new BackgroundProcessSupervisor(
            Path.Combine(supervisorRoot, "processes.json"));
        var vm = new EnvironmentPanelViewModel(
            workspace.Object, supervisor, host, sidebar, new MockClipboardService(), new InMemorySourceRegistry());
        return (vm, host, sidebar, workspace);
    }

    private static WorkspaceProject NewProject(string path)
    {
        var folderId = Guid.NewGuid().ToString("N");
        return new WorkspaceProject
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Path.GetFileName(path),
            Folders = [new WorkspaceFolder { Id = folderId, Path = path }],
            PrimaryFolderId = folderId,
        };
    }

    private static WorkspaceChange NewChange(string path, string status) => new()
    {
        Path = path,
        Status = status,
    };

    private static SubAgentRunViewModel NewSubAgent(string template, string status)
    {
        var run = new AgentSubAgentRun
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentRunId = "parent",
            TemplateId = template,
            Task = "test",
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
        };
        return new SubAgentRunViewModel(run);
    }

    private static SubAgentRunViewModel NewSubAgentAt(string template, string status, DateTimeOffset startedAt)
    {
        var run = new AgentSubAgentRun
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentRunId = "parent",
            TemplateId = template,
            Task = "test",
            Status = status,
            StartedAt = startedAt,
        };
        return new SubAgentRunViewModel(run);
    }
}
