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
