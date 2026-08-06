using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Projects;
using Moq;

namespace AIChat.Tests.Avalonia;

// Unit tests for the PR-3 extraction. The constructor only touches pure
// CLR types and an in-memory settings holder, so the tests do not need
// the Avalonia headless platform.
public class ProjectSidebarViewModelTests : IDisposable
{
    private readonly string _tempRoot;

    public ProjectSidebarViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AIChatSidebarTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public void Refresh_WithEmptyProjectList_LeavesCurrentProjectNull()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Refresh(Array.Empty<WorkspaceProject>());

        Assert.Null(vm.CurrentProject);
        Assert.Equal("未选择项目", vm.SelectedProjectName);
        Assert.Empty(vm.Projects);
    }

    [Fact]
    public void Refresh_WithProjects_PicksFirstNonEmptyAsCurrent()
    {
        var (vm, _, _) = CreateViewModel();
        var first = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "alpha") }], PrimaryFolderId = "f1"};
        var second = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "beta") }], PrimaryFolderId = "f1"};

        vm.Refresh([first, second]);

        Assert.Same(first, vm.CurrentProject);
        Assert.Equal(2, vm.Projects.Count);
        Assert.Equal("Alpha", vm.SelectedProjectName);
    }

    [Fact]
    public async Task SelectProject_WithKnownId_PersistsLastActiveAndRaisesEvent()
    {
        var (vm, repository, holder) = CreateViewModel();
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "alpha") }], PrimaryFolderId = "f1"};
        var beta = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "beta") }], PrimaryFolderId = "f1"};
        Mock.Get(repository).Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { alpha, beta });

        var captured = new List<ProjectSelectionChangedEventArgs>();
        vm.ProjectSelected += (_, args) => captured.Add(args);

        await vm.SelectProjectCommand.ExecuteAsync("b");

        var args = Assert.Single(captured);
        Assert.Same(beta, args.Project);
        Assert.Same(beta, vm.CurrentProject);
        Assert.Equal("b", holder.Current.LastActiveProjectId);
        Mock.Get(repository).Verify(repo => repo.SaveSettingsAsync(holder.Current, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SelectProject_WithUnknownId_DoesNothing()
    {
        var (vm, repository, _) = CreateViewModel();
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "alpha") }], PrimaryFolderId = "f1"};
        Mock.Get(repository).Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { alpha });

        await vm.SelectProjectCommand.ExecuteAsync("nonexistent");

        Assert.Null(vm.CurrentProject);
    }

    [Fact]
    public async Task AddProject_WithInvalidPath_RaisesFailedEvent()
    {
        var (vm, _, _) = CreateViewModel();

        var captured = new List<ProjectAddedEventArgs>();
        vm.ProjectAdded += (_, args) => captured.Add(args);

        await vm.AddProjectCommand.ExecuteAsync(@"C:\does-not-exist-zzz");

        var args = Assert.Single(captured);
        Assert.False(args.Succeeded);
        Assert.Equal("请选择存在的项目目录。", args.StatusMessage);
    }

    [Fact]
    public async Task AddProject_WithValidPath_CreatesWorkspaceAndRaisesEvent()
    {
        var (vm, repository, holder) = CreateViewModel();
        var alphaDir = Path.Combine(_tempRoot, "alpha");
        Directory.CreateDirectory(alphaDir);
        Mock.Get(repository).Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkspaceProject>());

        var captured = new List<ProjectAddedEventArgs>();
        vm.ProjectAdded += (_, args) => captured.Add(args);

        await vm.AddProjectCommand.ExecuteAsync(alphaDir);

        var args = Assert.Single(captured);
        Assert.True(args.Succeeded);
        Assert.NotNull(args.Project);
        Assert.Equal("alpha", args.Project!.Name);
        Assert.Equal(alphaDir, args.Project.TryGetPrimaryPath());
        Assert.Same(args.Project, vm.CurrentProject);
        Assert.Equal(args.Project.Id, holder.Current.LastActiveProjectId);

        Mock.Get(repository).Verify(repo => repo.SaveWorkspacesAsync(It.IsAny<List<WorkspaceProject>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveProject_RemovesFromListAndSaves()
    {
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/alpha" }], PrimaryFolderId = "f1"};
        var beta = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/beta" }], PrimaryFolderId = "f1"};
        var (vm, repository, holder) = CreateViewModel();
        Mock.Get(repository)
            .Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { alpha, beta });
        holder.Current.LastActiveProjectId = "a";
        vm.Refresh([alpha, beta]);
        Assert.Equal(2, vm.Projects.Count);

        var captured = new List<ProjectAddedEventArgs>();
        vm.ProjectAdded += (_, args) => captured.Add(args);

        await vm.RemoveProjectCommand.ExecuteAsync("a");

        Assert.Single(vm.Projects);
        Assert.Equal("b", vm.Projects[0].Id);
        Assert.Equal("", holder.Current.LastActiveProjectId);
        Mock.Get(repository).Verify(repo => repo.SaveWorkspacesAsync(
            It.Is<List<WorkspaceProject>>(list => list.Count == 1 && list[0].Id == "b"),
            It.IsAny<CancellationToken>()), Times.Once);
        var args = Assert.Single(captured);
        Assert.Contains("Alpha", args.StatusMessage);
    }

    [Fact]
    public async Task RemoveProject_WithUnknownId_DoesNothing()
    {
        var (vm, repository, _) = CreateViewModel();
        Mock.Get(repository)
            .Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/alpha" }], PrimaryFolderId = "f1"} });

        await vm.RemoveProjectCommand.ExecuteAsync("nope");

        Mock.Get(repository).Verify(repo => repo.SaveWorkspacesAsync(
            It.IsAny<List<WorkspaceProject>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveProject_WithEmptyId_DoesNothing()
    {
        var (vm, repository, _) = CreateViewModel();
        Mock.Get(repository)
            .Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkspaceProject>());

        await vm.RemoveProjectCommand.ExecuteAsync("");

        Mock.Get(repository).Verify(repo => repo.SaveWorkspacesAsync(
            It.IsAny<List<WorkspaceProject>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- 1.0.6: RemoveProject undo affordance ----

    private (ProjectSidebarViewModel vm, IAppRepository repository, SettingsHolder holder, ToastService toast)
        CreateViewModelWithToast()
    {
        var repository = Mock.Of<IAppRepository>();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var toast = new ToastService(action => action());
        var vm = new ProjectSidebarViewModel(repository, holder, toast);
        return (vm, repository, holder, toast);
    }

    [Fact]
    public async Task RemoveProject_ShowsUndoToast()
    {
        // Same contract as the conversation
        // delete path: a "已删除 X
        // [撤销]" toast appears with the
        // warning level, the action label
        // "撤销", and a callback wired
        // through the service's normal
        // click handler. The XAML renders
        // the button (HasAction=true) so
        // the user has a 3-second window
        // to undo a misclick.
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/alpha" }], PrimaryFolderId = "f1"};
        var beta = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/beta" }], PrimaryFolderId = "f1"};
        var (vm, repository, _, toast) = CreateViewModelWithToast();
        Mock.Get(repository)
            .Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { alpha, beta });
        vm.Refresh([alpha, beta]);

        await vm.RemoveProjectCommand.ExecuteAsync("a");

        var undoToast = Assert.Single(toast.Toasts);
        Assert.True(undoToast.HasAction);
        Assert.Equal("撤销", undoToast.ActionLabel);
        Assert.Equal(ToastLevel.Warning, undoToast.Level);
        Assert.Contains("Alpha", undoToast.Message);
    }

    [Fact]
    public async Task RemoveProject_UndoAction_RestoresActiveProject()
    {
        // Clicking "撤销" on the toast
        // re-inserts the deleted workspace
        // at the same list index, restores
        // the active pointer, and re-saves
        // both files. The user also gets
        // the workspace back as the active
        // project — without the
        // ApplyProject(snapshot) re-apply,
        // the sidebar would show the row
        // but the main panel would stay
        // in the "no project" state.
        //
        // The mock is wired with a
        // captured "current workspaces"
        // list rather than a fixed
        // ReturnsAsync array — without
        // the capture, both the delete
        // path and the restore path
        // would see the same initial
        // [alpha, beta] list (the
        // in-memory mutation in
        // RemoveProjectAsync is on a
        // .ToList() copy, not on the
        // mock's array), and RestoreProject
        // would short-circuit on the
        // "id already present" guard
        // because the mock keeps reporting
        // alpha as still in the list. The
        // callback rewires the captured
        // list on each Save, so a fresh
        // Load returns the post-save
        // state — which is what the
        // on-disk repo would do in
        // production.
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/alpha" }], PrimaryFolderId = "f1"};
        var beta = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/beta" }], PrimaryFolderId = "f1"};
        var (vm, repository, holder, toast) = CreateViewModelWithToast();
        var diskState = new List<WorkspaceProject> { alpha, beta };
        Mock.Get(repository)
            .Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => diskState.ToArray());
        Mock.Get(repository)
            .Setup(repo => repo.SaveWorkspacesAsync(It.IsAny<IReadOnlyList<WorkspaceProject>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<WorkspaceProject> list, CancellationToken _) =>
            {
                diskState.Clear();
                diskState.AddRange(list);
            })
            .Returns(Task.CompletedTask);
        holder.Current.LastActiveProjectId = "a";
        vm.Refresh([alpha, beta]);
        Assert.Equal(2, vm.Projects.Count);

        await vm.RemoveProjectCommand.ExecuteAsync("a");
        Assert.Single(vm.Projects);
        Assert.Equal("b", vm.Projects[0].Id);
        Assert.Equal("", holder.Current.LastActiveProjectId);

        // The user clicks "撤销" on the toast.
        // RestoreProject runs as a fire-and-
        // forget Task — the action callback
        // discards it. The test polls the
        // visible state to avoid a fixed
        // delay dependency.
        toast.Toasts[0].InvokeAction();

        // The restore path is itself async
        // (re-loads workspaces, re-saves, then
        // re-applies the active project). We
        // poll for the visible state to land
        // so the test does not depend on a
        // fixed delay.
        for (var i = 0; i < 100 && vm.Projects.Count != 2; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, vm.Projects.Count);
        Assert.Contains(vm.Projects, card => card.Id == "a");
        Assert.Contains(vm.Projects, card => card.Id == "b");
        Assert.Equal("a", holder.Current.LastActiveProjectId);
        Assert.Same(alpha, vm.CurrentProject);
    }

    [Fact]
    public async Task RemoveProject_WithoutToastService_StillDeletes()
    {
        // Regression guard: the IToastService
        // ctor parameter is optional so the
        // 8 existing test sites that build
        // the VM directly don't have to
        // wire a mock. The delete must
        // still work when no toast service
        // is present.
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/alpha" }], PrimaryFolderId = "f1"};
        var beta = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/beta" }], PrimaryFolderId = "f1"};
        var (vm, repository, holder) = CreateViewModel();
        Mock.Get(repository)
            .Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { alpha, beta });
        holder.Current.LastActiveProjectId = "a";
        vm.Refresh([alpha, beta]);

        await vm.RemoveProjectCommand.ExecuteAsync("a");

        Assert.Single(vm.Projects);
        Assert.Equal("b", vm.Projects[0].Id);
        Assert.Equal("", holder.Current.LastActiveProjectId);
        Mock.Get(repository).Verify(repo => repo.SaveWorkspacesAsync(
            It.IsAny<List<WorkspaceProject>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Refresh_WithProjects_RaisesProjectSelected_ForStartupRestore()
    {
        // The startup path (MainWindowVM.RefreshAsync -> Sidebar.Refresh)
        // silently updated CurrentProject and didn't fire ProjectSelected
        // pre-fix, so the FileTreeViewModel's "rebuild on project change"
        // subscription never fired and the file tree stayed empty until
        // the user clicked the project card again. ApplyProject now
        // raises ProjectSelected on every CurrentProject transition
        // (including the null → real-project one on cold start), so
        // this test pins the post-fix behavior.
        var (vm, _, _) = CreateViewModel();
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "alpha") }], PrimaryFolderId = "f1"};
        var beta = new WorkspaceProject { Id = "b", Name = "Beta", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "beta") }], PrimaryFolderId = "f1"};
        var captured = new List<ProjectSelectionChangedEventArgs>();
        vm.ProjectSelected += (_, args) => captured.Add(args);

        vm.Refresh([alpha, beta]);

        var args = Assert.Single(captured);
        Assert.Same(alpha, args.Project);
        Assert.Equal("已切换到项目：Alpha", args.StatusMessage);
    }

    [Fact]
    public void Refresh_WithSameProject_DoesNotRaiseProjectSelected()
    {
        // Re-raise would double-rebuild the file tree + recompute the
        // context budget on every "save settings" round-trip that
        // happens to land on the same CurrentProject. ReferenceEquals
        // guard inside ApplyProject prevents this.
        var (vm, _, _) = CreateViewModel();
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "alpha") }], PrimaryFolderId = "f1"};
        var captured = new List<ProjectSelectionChangedEventArgs>();
        vm.Refresh([alpha]);
        vm.ProjectSelected += (_, args) => captured.Add(args);

        // Second refresh with the same project list — CurrentProject
        // is the same reference, no transition, no event.
        vm.Refresh([alpha]);

        Assert.Empty(captured);
    }

    [Fact]
    public void Refresh_WithEmptyList_AfterProjects_RaisesProjectSelected_WithNull()
    {
        // The "user removes the last project" path needs to fire
        // ProjectSelected with Project=null so FileTreeViewModel can
        // clear its Root and the sidebar can show the "no project"
        // hint instead of stale data. Pre-fix this transition was
        // silent because ApplyProject never fired on the null path.
        var (vm, _, _) = CreateViewModel();
        var alpha = new WorkspaceProject { Id = "a", Name = "Alpha", Folders = [new WorkspaceFolder { Id = "f1", Path = Path.Combine(_tempRoot, "alpha") }], PrimaryFolderId = "f1"};
        var captured = new List<ProjectSelectionChangedEventArgs>();
        vm.Refresh([alpha]);
        vm.ProjectSelected += (_, args) => captured.Add(args);

        vm.Refresh(Array.Empty<WorkspaceProject>());

        var args = Assert.Single(captured);
        Assert.Null(args.Project);
    }

    private (ProjectSidebarViewModel vm, IAppRepository repository, SettingsHolder holder) CreateViewModel()
    {
        var repository = Mock.Of<IAppRepository>();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var vm = new ProjectSidebarViewModel(repository, holder);
        return (vm, repository, holder);
    }
}
