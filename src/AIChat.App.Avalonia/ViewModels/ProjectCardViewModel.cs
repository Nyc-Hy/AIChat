using System.Collections.ObjectModel;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the sidebar's 项目 list. Id is the project record id
// (used by the XAML Tag + the SelectProjectAsync path), Path is the
// primary folder's on-disk root (kept for backwards compatibility
// with single-folder callers), Name is what the user sees. IsSelected
// drives the .selected class on the sidebar-row style.
//
// Wave 3 (plan §3.2): multi-folder support. A workspace can have N
// folders, one of which is "primary". Folders + PrimaryFolderId are
// surfaced here so the project card and popover can render the full
// list + a "set as primary" affordance. FolderCount drives the
// "📁 N" badge in the sidebar row when N > 1.
public sealed partial class ProjectCardViewModel : ObservableObject
{
    public ProjectCardViewModel(string id, string name, string primaryPath)
    {
        Id = id;
        Name = name;
        _path = primaryPath;
    }

    public string Id { get; }
    public string Name { get; set; }
    private string _path;
    public string Path
    {
        get => _path;
        set
        {
            if (_path == value) return;
            _path = value;
            OnPropertyChanged(nameof(Path));
        }
    }

    [ObservableProperty]
    private bool isSelected;

    public ObservableCollection<ProjectFolderItemViewModel> Folders { get; } = [];

    public string PrimaryFolderId { get; set; } = "";

    public int FolderCount => Folders.Count;

    public bool IsMultiFolder => Folders.Count > 1;

    // Text rendered next to the project name in the sidebar row when
    // the project has more than one folder. "" for single-folder
    // workspaces so the steady state stays clean.
    public string MultiFolderBadge => IsMultiFolder ? $"📁 {Folders.Count}" : "";

    // Wire Folders + PrimaryFolderId from a workspace; called on
    // every Refresh so the badge + popover stay in sync with the
    // persisted shape. We materialize WorkspaceFolder into a
    // ProjectFolderItemViewModel so XAML can bind IsPrimary without a
    // converter / multibinding against PrimaryFolderId.
    public void SyncFolders(WorkspaceProject workspace)
    {
        Folders.Clear();
        foreach (var folder in workspace.Folders)
        {
            Folders.Add(new ProjectFolderItemViewModel
            {
                Id = folder.Id,
                Path = folder.Path,
                IsPrimary = string.Equals(folder.Id, workspace.PrimaryFolderId, StringComparison.OrdinalIgnoreCase),
            });
        }
        PrimaryFolderId = workspace.PrimaryFolderId;
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(IsMultiFolder));
        OnPropertyChanged(nameof(MultiFolderBadge));
    }
}

// One folder row inside a ProjectCardViewModel popover. Wraps
// WorkspaceFolder with a precomputed IsPrimary flag so the XAML can
// bind {Binding IsPrimary} without a converter.
public sealed class ProjectFolderItemViewModel
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsPrimary { get; set; }
}
