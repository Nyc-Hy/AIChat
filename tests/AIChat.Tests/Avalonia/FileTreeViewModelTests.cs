using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Workspace;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Avalonia;

// Unit tests for the file-tree VM. The IProjectFileIndexFactory
// is stubbed so the test doesn't touch disk; the VM's
// orchestration (subscribe to sidebar, rebuild on project change,
// cancel in-flight builds when a newer one starts) is the part
// the test actually locks down.
public class FileTreeViewModelTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly FileTreeViewModel _vm;
    private readonly RecordingFileOpener _opener = new();

    public FileTreeViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "aichat-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src"));
        File.WriteAllText(Path.Combine(_tempRoot, "src", "Foo.cs"), "// hello\n");
        File.WriteAllText(Path.Combine(_tempRoot, "README.md"), "# readme\n");

        _sidebar = new ProjectSidebarViewModel(
            new StubRepository(),
            new StubSettingsHolder());
        _vm = new FileTreeViewModel(_sidebar, new StubFactory(_tempRoot), _opener);
    }

    public void Dispose()
    {
        _vm.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task RebuildAsync_WithValidPath_PopulatesRoot()
    {
        await _vm.RebuildAsync(_tempRoot);

        Assert.NotNull(_vm.Root);
        Assert.Equal(_tempRoot, _vm.RootPath);
        Assert.False(_vm.IsBuilding);
        Assert.Null(_vm.BuildError);
    }

    [Fact]
    public async Task RebuildAsync_WithInvalidPath_LeavesRootNull()
    {
        await _vm.RebuildAsync("/nonexistent/path/that/does/not/exist");

        Assert.Null(_vm.Root);
        Assert.False(_vm.IsBuilding);
    }

    [Fact]
    public async Task RebuildAsync_AfterIndexFailure_SurfacesError()
    {
        var failing = new FileTreeViewModel(_sidebar, new FailingFactory(), _opener);
        try
        {
            await failing.RebuildAsync(_tempRoot);

            Assert.Null(failing.Root);
            Assert.NotNull(failing.BuildError);
            Assert.False(failing.IsBuilding);
        }
        finally
        {
            failing.Dispose();
        }
    }

    [Fact]
    public void SelectFile_WithFolder_DoesNotRaiseEvent()
    {
        // Folders are containers, not files; selecting one is a
        // navigation action, not a file-pick. The VM must filter
        // those out so the host doesn't open a "preview" for a
        // directory.
        var raised = 0;
        _vm.FileSelected += (_, _) => raised++;
        var folder = new FileTreeNodeViewModel("src", "src", isFolder: true, sizeBytes: 0, typeTag: "")
        {
            FileCount = 3,
        };

        _vm.SelectFileCommand.Execute(folder);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void SelectFile_WithFile_RaisesEvent()
    {
        var captured = new List<FileTreeFileSelectedEventArgs>();
        _vm.FileSelected += (_, args) => captured.Add(args);
        var file = new FileTreeNodeViewModel("Foo.cs", "src/Foo.cs", isFolder: false, sizeBytes: 100, typeTag: "source");

        _vm.SelectFileCommand.Execute(file);

        var raised = Assert.Single(captured);
        Assert.Equal("src/Foo.cs", raised.RelativePath);
        Assert.Equal("Foo.cs", raised.DisplayName);
    }

    [Fact]
    public void ToggleFolder_FlipsIsExpanded()
    {
        var folder = new FileTreeNodeViewModel("src", "src", isFolder: true, sizeBytes: 0, typeTag: "")
        {
            IsExpanded = false,
        };

        _vm.ToggleFolderCommand.Execute(folder);
        Assert.True(folder.IsExpanded);

        _vm.ToggleFolderCommand.Execute(folder);
        Assert.False(folder.IsExpanded);
    }

    [Fact]
    public void ToggleFolder_OnFile_IsNoOp()
    {
        // Capture the file's IsExpanded before invoking the
        // command, then assert the same value afterwards. Files
        // don't have an IsExpanded concept; the command should
        // not flip any state. The default initial value of
        // IsExpanded happens to be true (folder default), so
        // we don't assert on a specific value — only that the
        // value is unchanged.
        var file = new FileTreeNodeViewModel("Foo.cs", "src/Foo.cs", isFolder: false, sizeBytes: 100, typeTag: "source");
        var before = file.IsExpanded;

        _vm.ToggleFolderCommand.Execute(file);

        Assert.Equal(before, file.IsExpanded);
    }

    [Fact]
    public void OpenWithSystemApp_OnFile_CallsOpenerWithAbsolutePath()
    {
        // Set the sidebar's current project so the relative
        // path resolves to the temp root.
        var project = new ProjectWorkspace { Id = "p1", Name = "TreeTest", Path = _tempRoot };
        _sidebar.Refresh(new[] { project });
        _sidebar.SelectProjectCommand.Execute(project.Id);

        var file = new FileTreeNodeViewModel("Foo.cs", "src/Foo.cs", isFolder: false, sizeBytes: 100, typeTag: "source");

        _vm.OpenWithSystemAppCommand.Execute(file);

        var opened = Assert.Single(_opener.OpenedPaths);
        var expected = Path.Combine(_tempRoot, "src", "Foo.cs");
        Assert.Equal(expected, opened);
    }

    [Fact]
    public void OpenWithSystemApp_OnFolder_IsNoOp()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "TreeTest", Path = _tempRoot };
        _sidebar.Refresh(new[] { project });
        _sidebar.SelectProjectCommand.Execute(project.Id);

        var folder = new FileTreeNodeViewModel("src", "src", isFolder: true, sizeBytes: 0, typeTag: "");

        _vm.OpenWithSystemAppCommand.Execute(folder);

        Assert.Empty(_opener.OpenedPaths);
    }

    [Fact]
    public void OpenWithSystemApp_WhenOpenerThrows_SurfacesStatusMessage()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "TreeTest", Path = _tempRoot };
        _sidebar.Refresh(new[] { project });
        _sidebar.SelectProjectCommand.Execute(project.Id);
        _opener.NextError = new InvalidOperationException("simulated OS denial");

        var captured = new List<string>();
        _vm.StatusMessageRequested += (_, message) => captured.Add(message);
        var file = new FileTreeNodeViewModel("Foo.cs", "src/Foo.cs", isFolder: false, sizeBytes: 100, typeTag: "source");

        _vm.OpenWithSystemAppCommand.Execute(file);

        var raised = Assert.Single(captured);
        Assert.Contains("simulated OS denial", raised);
    }

    [Fact]
    public void OpenWithSystemApp_WithNoProject_SurfacesStatusMessage()
    {
        // No project selected — no path to resolve against.
        var captured = new List<string>();
        _vm.StatusMessageRequested += (_, message) => captured.Add(message);
        var file = new FileTreeNodeViewModel("Foo.cs", "src/Foo.cs", isFolder: false, sizeBytes: 100, typeTag: "source");

        _vm.OpenWithSystemAppCommand.Execute(file);

        Assert.NotEmpty(captured);
        Assert.Empty(_opener.OpenedPaths);
    }

    // Stub IFileOpener that records every path it was asked to
    // open and can be configured to throw on the next call. The
    // test host runs on macOS / Linux / Windows equally so we
    // can't use the real Mac opener without spawning processes
    // (and possibly opening files in user-visible apps).
    private sealed class RecordingFileOpener : AIChat.App.Avalonia.Composition.IFileOpener
    {
        public List<string> OpenedPaths { get; } = new();
        public Exception? NextError { get; set; }

        public void OpenWithSystemApp(string absolutePath)
        {
            if (NextError is not null)
            {
                var err = NextError;
                NextError = null;
                throw err;
            }
            OpenedPaths.Add(absolutePath);
        }
    }

    // In-memory IProjectFileIndexFactory that returns a fixed
    // tiny tree — keeps the test independent of the disk-walking
    // production builder.
    private sealed class StubFactory(string root) : IProjectFileIndexFactory
    {
        public ProjectFileIndex Build(string rootPath) => new()
        {
            RootPath = root,
            Entries = new List<ProjectFileIndexEntry>
            {
                new() { RelativePath = "src/Foo.cs", SizeBytes = 1, Extension = ".cs", TypeTag = "source" },
                new() { RelativePath = "README.md", SizeBytes = 1, Extension = ".md", TypeTag = "doc" }
            }
        };
    }

    private sealed class FailingFactory : IProjectFileIndexFactory
    {
        public ProjectFileIndex Build(string rootPath) =>
            throw new InvalidOperationException("simulated disk failure");
    }

    private sealed class StubRepository : AIChat.Abstractions.Persistence.IAppRepository
    {
        public Task<AIChat.Abstractions.Configuration.AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AIChat.Abstractions.Configuration.AppSettings());
        public Task SaveSettingsAsync(AIChat.Abstractions.Configuration.AppSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectWorkspace>> LoadProjectsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<ProjectWorkspace>)Array.Empty<ProjectWorkspace>());
        public Task SaveProjectsAsync(IReadOnlyList<ProjectWorkspace> projects, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubSettingsHolder : AIChat.App.Avalonia.Composition.ISettingsHolder
    {
        public AIChat.Abstractions.Configuration.AppSettings Current { get; private set; } = new();
        public void Replace(AIChat.Abstractions.Configuration.AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Current = settings;
        }
    }
}
