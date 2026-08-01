using System.Collections.ObjectModel;
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
    private CancellationTokenSource? _currentBuildCts;
    private bool _disposed;

    // Raised when the user clicks (single-click) a file leaf so
    // the host can pop the preview pane or wire a tool. The host
    // decides what to do; the tree just signals the intent.
    public event EventHandler<FileTreeFileSelectedEventArgs>? FileSelected;

    [ObservableProperty]
    private FileTreeNodeViewModel? root;

    [ObservableProperty]
    private string rootPath = "";

    [ObservableProperty]
    private bool isBuilding;

    [ObservableProperty]
    private string? buildError;

    public FileTreeViewModel(ProjectSidebarViewModel sidebar, IProjectFileIndexFactory indexFactory)
    {
        _sidebar = sidebar;
        _indexFactory = indexFactory;
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
            Root = null;
            RootPath = "";
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
