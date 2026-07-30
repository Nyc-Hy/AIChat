using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Workspace;
using AIChat.Domain.Projects;
using Moq;

namespace AIChat.Tests.Avalonia;

// Unit tests for the git status / diff viewer. The workspace service
// is mocked so the tests run synchronously and in-memory — no actual
// git invocation. ProjectSidebarViewModel is a real instance so the
// property-change wiring (CurrentProject) is exercised.
public class GitStatusViewModelTests
{
    [Fact]
    public async Task RefreshAsync_WithNoProject_LeavesStateEmpty()
    {
        var (vm, _, _) = CreateViewModel(currentProject: null);

        await vm.RefreshAsync();

        Assert.False(vm.IsAvailable);
        Assert.Equal("", vm.Branch);
        Assert.Empty(vm.Changes);
        Assert.Null(vm.SelectedChange);
        Assert.False(vm.HasChanges);
        Assert.Null(vm.DiffText);
    }

    [Fact]
    public async Task RefreshAsync_PopulatesBranchAndChanges()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "## main...origin/main [ahead 1]",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "src/Program.cs" },
                new WorkspaceChange { Status = "?? ", Path = "src/New.cs" },
                new WorkspaceChange { Status = "A ", Path = "src/Added.cs" }
            ]
        };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        Mock.Get(workspace)
            .Setup(w => w.GetChangesAsync(project.Path, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeSet);
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();

        Assert.Equal("main...origin/main [ahead 1]", vm.Branch);
        Assert.Equal(3, vm.Changes.Count);
        Assert.True(vm.HasChanges);
        Assert.NotNull(vm.SelectedChange);
        Assert.Equal("src/Program.cs", vm.SelectedChange!.Path);
    }

    [Fact]
    public async Task RefreshAsync_WithCleanWorkingTree_ShowsEmptyState()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        Mock.Get(workspace)
            .Setup(w => w.GetChangesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceChangeSet { Branch = "## main", Changes = [] });
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();

        Assert.Equal("main", vm.Branch);
        Assert.Empty(vm.Changes);
        Assert.False(vm.HasChanges);
        Assert.Equal("(工作区干净，没有未提交改动)", vm.EmptyStateMessage);
    }

    [Fact]
    public async Task RefreshAsync_WhenWorkspaceThrows_SetsErrorMessage()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        Mock.Get(workspace)
            .Setup(w => w.GetChangesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not a git repo"));
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("not a git repo", vm.ErrorMessage);
    }

    [Fact]
    public async Task SelectChangeAsync_LoadsDiffForSelectedFile()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "## main",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "src/Program.cs" },
                new WorkspaceChange { Status = "A ", Path = "src/Added.cs" }
            ]
        };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        Mock.Get(workspace)
            .Setup(w => w.GetChangesAsync(project.Path, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeSet);
        Mock.Get(workspace)
            .Setup(w => w.GetDiffAsync(project.Path, "src/Added.cs", false, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceDiff
            {
                Path = "src/Added.cs",
                DiffText = "@@ -0,0 +1,3 @@\n+// new file\n+Console.WriteLine();\n+",
                IsTruncated = false
            });
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();
        // Default selection lands on the first change.
        Assert.Equal("src/Program.cs", vm.SelectedChange!.Path);

        var second = vm.Changes.Single(c => c.Path == "src/Added.cs");
        await vm.SelectChangeCommand.ExecuteAsync(second);

        Assert.Same(second, vm.SelectedChange);
        Assert.Contains("// new file", vm.DiffText);
        Assert.True(vm.HasDiff);
        Assert.False(vm.IsDiffTruncated);
        Assert.Equal("src/Added.cs", vm.SelectedPath);
    }

    [Fact]
    public async Task SelectChangeAsync_WhenDiffFails_SetsErrorDiffText()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "## main",
            Changes = [new WorkspaceChange { Status = "M ", Path = "src/Locked.cs" }]
        };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        Mock.Get(workspace)
            .Setup(w => w.GetChangesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeSet);
        Mock.Get(workspace)
            .Setup(w => w.GetDiffAsync(It.IsAny<string>(), "src/Locked.cs", It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("file locked"));
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();

        Assert.NotNull(vm.DiffText);
        Assert.Contains("diff 读取失败", vm.DiffText);
        Assert.Contains("file locked", vm.DiffText);
    }

    [Fact]
    public async Task RefreshAsync_RestoresSelection_WhenFileStillPresent()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var first = new WorkspaceChangeSet
        {
            Branch = "## main",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "src/A.cs" },
                new WorkspaceChange { Status = "M ", Path = "src/B.cs" }
            ]
        };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        var setup = Mock.Get(workspace);
        setup.Setup(w => w.GetChangesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();
        var second = vm.Changes.Single(c => c.Path == "src/B.cs");
        await vm.SelectChangeCommand.ExecuteAsync(second);
        Assert.Equal("src/B.cs", vm.SelectedPath);

        // Re-fetch with the same file list — selection should stick.
        await vm.RefreshAsync();
        Assert.NotNull(vm.SelectedChange);
        Assert.Equal("src/B.cs", vm.SelectedChange!.Path);
    }

    [Fact]
    public async Task StatusKind_MapsPorcelainCodes()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var changeSet = new WorkspaceChangeSet
        {
            Branch = "## main",
            Changes =
            [
                new WorkspaceChange { Status = "M ", Path = "modified.cs" },
                new WorkspaceChange { Status = "A ", Path = "added.cs" },
                new WorkspaceChange { Status = "D ", Path = "deleted.cs" },
                new WorkspaceChange { Status = "R ", Path = "renamed.cs" },
                new WorkspaceChange { Status = "??", Path = "untracked.cs" }
            ]
        };
        var workspace = Mock.Of<IWorkspaceChangeService>();
        Mock.Get(workspace)
            .Setup(w => w.GetChangesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeSet);
        var (vm, _, _) = CreateViewModel(currentProject: project, workspace: workspace);

        await vm.RefreshAsync();

        var byPath = vm.Changes.ToDictionary(c => c.Path);
        Assert.Equal("modified", byPath["modified.cs"].StatusKind);
        Assert.Equal("added", byPath["added.cs"].StatusKind);
        Assert.Equal("deleted", byPath["deleted.cs"].StatusKind);
        Assert.Equal("renamed", byPath["renamed.cs"].StatusKind);
        Assert.Equal("untracked", byPath["untracked.cs"].StatusKind);
    }

    private static (GitStatusViewModel vm, IWorkspaceChangeService workspace, ProjectSidebarViewModel sidebar) CreateViewModel(
        ProjectWorkspace? currentProject,
        IWorkspaceChangeService? workspace = null)
    {
        workspace ??= Mock.Of<IWorkspaceChangeService>();
        var repository = Mock.Of<IAppRepository>();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, holder);
        if (currentProject is not null)
        {
            sidebar.Refresh([currentProject]);
        }
        else
        {
            sidebar.Refresh([]);
        }
        var vm = new GitStatusViewModel(workspace, sidebar);
        return (vm, workspace, sidebar);
    }
}
