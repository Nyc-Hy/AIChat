using System.Collections.ObjectModel;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the file-tree state for the current project. Listens to
// the sidebar's project selection and rebuilds the tree when the
// project changes. The actual index is built by
// ProjectFileIndexBuilder (already used by the agent); this VM
// just runs that, passes the result through FileTreeBuilder, and
// exposes the converted ViewModels to the XAML.
//
// Performance note: ProjectFileIndexBuilder + FileTreeBuilder
// both walk the disk. We do this off the UI thread via
// Task.Run so the sidebar doesn't freeze on a large repo.
// CancellationToken plumbed through so a fast project switch
// cancels the in-flight index.
public sealed partial class FileTreeViewModel : ViewModelBase, IDisposable
{
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly IProjectFileIndexFactory _indexFactory;
    private readonly IFileOpener _fileOpener;
    private CancellationTokenSource? _currentBuildCts;
    private bool _disposed;

    // Raised when the user clicks (single-click) a file leaf so
    // the host can pop the preview pane or wire a tool. The host
    // decides what to do; the tree just signals the intent.
    public event EventHandler<FileTreeFileSelectedEventArgs>? FileSelected;

    // Raised when the opener (or anything else in the tree VM)
    // wants the host to surface a status message — the host
    // already has a status bar / toast surface and the tree
    // doesn't need its own.
    public event EventHandler<string>? StatusMessageRequested;

    [ObservableProperty]
    private FileTreeNodeViewModel? root;

    [ObservableProperty]
    private string rootPath = "";

    [ObservableProperty]
    private bool isBuilding;

    [ObservableProperty]
    private string? buildError;

    public FileTreeViewModel(
        ProjectSidebarViewModel sidebar,
        IProjectFileIndexFactory indexFactory,
        IFileOpener fileOpener)
    {
        _sidebar = sidebar;
        _indexFactory = indexFactory;
        _fileOpener = fileOpener;
        _sidebar.ProjectSelected += OnProjectSelected;
    }

    private void OnProjectSelected(object? sender, ProjectSelectionChangedEventArgs args)
    {
        // Async void because ProjectSelected is a synchronous event;
        // any exception would become an unobserved task. Wrap the
        // body in try/catch so the build error path is the only
        // surface for failures.
        _ = RebuildAsync(args.Project?.Path);
    }

    public async Task RebuildAsync(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            // No project (or the project directory was deleted between
            // sessions). Reset every piece of state the XAML binds to
            // so the sidebar shows the "select a project" hint and
            // nothing else — pre-fix, IsBuilding could stay true from
            // a cancelled build of a *previous* project, so the user
            // would see "正在建立文件索引…" and the (empty) tree at
            // the same time.
            Root = null;
            RootPath = "";
            IsBuilding = false;
            BuildError = null;
            return;
        }

        // Cancel any in-flight build for a previous project so a
        // fast project switch doesn't waste disk walks.
        _currentBuildCts?.Cancel();
        _currentBuildCts = new CancellationTokenSource();
        var token = _currentBuildCts.Token;

        IsBuilding = true;
        BuildError = null;
        RootPath = projectPath;

        try
        {
            var index = await Task.Run(() => _indexFactory.Build(projectPath), token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            var tree = FileTreeBuilder.Build(index);
            Root = FileTreeNodeViewModel.From(tree);
        }
        catch (OperationCanceledException)
        {
            // A newer rebuild superseded this one — silently drop.
        }
        catch (Exception ex)
        {
            BuildError = ex.Message;
            Root = null;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBuilding = false;
            }
        }
    }

    // Forwarded from FileTreeNodeViewModel by the XAML code-behind
    // when a leaf is selected. The XAML passes the node; the VM
    // re-raises as a strongly-typed event so the host doesn't need
    // to know about FileTreeNodeViewModel.
    [RelayCommand]
    private void SelectFile(FileTreeNodeViewModel? node)
    {
        if (node is null || node.IsFolder)
        {
            return;
        }
        FileSelected?.Invoke(this, new FileTreeFileSelectedEventArgs(node.RelativePath, node.Name));
    }

    [RelayCommand]
    private void ToggleFolder(FileTreeNodeViewModel? node)
    {
        if (node is null || !node.IsFolder)
        {
            return;
        }
        node.IsExpanded = !node.IsExpanded;
    }

    // Open the file in the system default app (the user's IDE,
    // their editor, Preview for images, etc.). Triggered by
    // double-click in the XAML. Resolves the relative path
    // against the current project root, calls IFileOpener,
    // and surfaces any failure via StatusMessageRequested so
    // the user knows why nothing happened (the host can route
    // this into the existing status bar or toast surface).
    [RelayCommand]
    private void OpenWithSystemApp(FileTreeNodeViewModel? node)
    {
        if (node is null || node.IsFolder)
        {
            return;
        }
        var projectRoot = _sidebar.CurrentProject?.Path;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            StatusMessageRequested?.Invoke(this, "没有当前项目，无法打开文件。");
            return;
        }
        var fullPath = Path.Combine(projectRoot, node.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            _fileOpener.OpenWithSystemApp(fullPath);
        }
        catch (Exception ex)
        {
            StatusMessageRequested?.Invoke(this, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _sidebar.ProjectSelected -= OnProjectSelected;
        _currentBuildCts?.Cancel();
        _currentBuildCts?.Dispose();
    }
}

public sealed class FileTreeFileSelectedEventArgs(string relativePath, string displayName) : EventArgs
{
    public string RelativePath { get; } = relativePath;
    public string DisplayName { get; } = displayName;
}

// Thin wrapper so the test host can pass a synchronous in-memory
// implementation without pulling the real disk-walking
// ProjectFileIndexBuilder into the unit-test process.
public interface IProjectFileIndexFactory
{
    ProjectFileIndex Build(string rootPath);
}
