using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Memory;
using AIChat.Domain.Projects;
using Moq;

namespace AIChat.Tests.Avalonia;

// Unit tests for the memory editor. The editor's job is straightforward:
// add a validated entry to the current project, delete an entry, persist
// the full project list back. We mock IAppRepository so the tests stay
// synchronous and in-memory; ProjectSidebarViewModel is a real instance
// so the property-change wiring (CurrentProject) is exercised.
public class MemoryEditorViewModelTests
{
    [Fact]
    public void Refresh_WithNoCurrentProject_LeavesEntriesEmpty()
    {
        var (vm, _, _) = CreateViewModel(currentProject: null);

        Assert.False(vm.IsAvailable);
        Assert.Empty(vm.Entries);
        Assert.Equal(0, vm.EntryCount);
        Assert.Equal("", vm.ProjectName);
    }

    [Fact]
    public void Refresh_WithCurrentProject_PopulatesEntriesNewestFirst()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Memories =
            [
                new MemoryEntry { Id = "m1", ProjectId = "p1", Category = MemoryCategory.Project, Content = "first", CreatedAt = DateTimeOffset.Now.AddDays(-2), UpdatedAt = DateTimeOffset.Now.AddDays(-2) },
                new MemoryEntry { Id = "m2", ProjectId = "p1", Category = MemoryCategory.User, Content = "second", CreatedAt = DateTimeOffset.Now.AddDays(-1), UpdatedAt = DateTimeOffset.Now.AddDays(-1) }
            ]
        };
        var (vm, _, _) = CreateViewModel(currentProject: project);

        Assert.True(vm.IsAvailable);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("Alpha", vm.ProjectName);
        // Newest first — m2 was updated more recently.
        Assert.Equal("m2", vm.Entries[0].Source.Id);
        Assert.Equal("m1", vm.Entries[1].Source.Id);
    }

    [Fact]
    public async Task AddAsync_WithValidContent_AppendsToProjectAndSaves()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var repository = Mock.Of<IAppRepository>();
        Mock.Get(repository)
            .Setup(repo => repo.LoadProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { project });
        var (vm, _, _) = CreateViewModel(currentProject: project, repository: repository);

        vm.NewContent = "user prefers tabs over spaces";
        vm.NewCategory = MemoryCategory.User;

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Single(project.Memories);
        Assert.Equal("user prefers tabs over spaces", project.Memories[0].Content);
        Assert.Equal(MemoryCategory.User, project.Memories[0].Category);
        Assert.Equal("", vm.NewContent);
        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.Entries);
        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.Is<List<ProjectWorkspace>>(list => list.Count == 1 && list[0].Memories.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_WithEmptyContent_DoesNothing()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var (vm, _, _) = CreateViewModel(currentProject: project);

        vm.NewContent = "   ";

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Empty(project.Memories);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddAsync_WithSecretContent_SetsErrorMessage()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var (vm, _, _) = CreateViewModel(currentProject: project);

        vm.NewContent = "my API_KEY=sk-abcdef12345";

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Empty(project.Memories);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("secret", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddCommand_DisablesItselfAfterValidationError()
    {
        // CanAdd includes "ErrorMessage is null or empty" so a failed
        // add (secret detection, validation rule, ...) must immediately
        // disable the Add button — otherwise the user can click it
        // repeatedly and just see the same red error every time. The
        // [NotifyCanExecuteChangedFor] on ErrorMessage is what makes
        // this work without OnNewContentChanged having to fire first.
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var (vm, _, _) = CreateViewModel(currentProject: project);

        vm.NewContent = "my API_KEY=sk-abcdef12345";
        await vm.AddCommand.ExecuteAsync(null);

        // ErrorMessage is now non-null, the content is still
        // non-empty — the user has no way to know the button is
        // disabled unless CanExecute re-evaluates.
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.AddCommand.CanExecute(null));

        // Typing again clears ErrorMessage via OnNewContentChanged
        // and the button re-enables.
        vm.NewContent = "real content this time";
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntryAndSaves()
    {
        var entry = new MemoryEntry
        {
            Id = "m1",
            ProjectId = "p1",
            Category = MemoryCategory.Project,
            Content = "obsolete",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Memories = [entry]
        };
        var repository = Mock.Of<IAppRepository>();
        Mock.Get(repository)
            .Setup(repo => repo.LoadProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { project });
        var (vm, _, _) = CreateViewModel(currentProject: project, repository: repository);

        var rowVm = Assert.Single(vm.Entries);
        await rowVm.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(project.Memories);
        Assert.Empty(vm.Entries);
        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.Is<List<ProjectWorkspace>>(list => list.Count == 1 && list[0].Memories.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CanAdd_TracksNewContentAndProjectPresence()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var (vm, _, _) = CreateViewModel(currentProject: null);

        // No project → can't add even with text.
        vm.NewContent = "anything";
        Assert.False(vm.AddCommand.CanExecute(null));

        // Simulate project becoming available by re-creating the VM
        // with a real sidebar that holds the project. The simplest
        // path here is to construct a fresh editor with the project.
        var (vmWithProject, _, _) = CreateViewModel(currentProject: project);
        Assert.False(vmWithProject.AddCommand.CanExecute(null));

        vmWithProject.NewContent = "real content";
        Assert.True(vmWithProject.AddCommand.CanExecute(null));
    }

    private static (MemoryEditorViewModel editor, IAppRepository repository, SettingsHolder holder) CreateViewModel(
        ProjectWorkspace? currentProject,
        IAppRepository? repository = null)
    {
        repository ??= Mock.Of<IAppRepository>();
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
        var editor = new MemoryEditorViewModel(repository, sidebar);
        return (editor, repository, holder);
    }
}
