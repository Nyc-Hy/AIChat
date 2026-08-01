using System.Collections.ObjectModel;
using AIChat.Application.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the file tree. The Avalonia TreeView is data-templated
// off a single node type; folders and files share the same shape
// (they only differ in Children count + the chevron), so a single
// ViewModel covers both. SelectedFile is the "user clicked me"
// event raised by the XAML code-behind so the host can decide
// whether to open a preview or run a tool.
public sealed partial class FileTreeNodeViewModel(
    string name,
    string relativePath,
    bool isFolder,
    long sizeBytes,
    string typeTag) : ObservableObject
{
    public string Name { get; } = name;
    public string RelativePath { get; } = relativePath;
    public bool IsFolder { get; } = isFolder;
    public long SizeBytes { get; } = sizeBytes;
    public string TypeTag { get; } = typeTag;

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private bool isSelected;

    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = new();

    // The folder FileCount surfaced in the XAML as "src/ (12)" so
    // the user can tell at a glance how big a directory is without
    // expanding it. Updated by the parent during construction.
    public int FileCount { get; set; }

    // Indicator used by the row template to pick a glyph.
    // "folder" for folders, otherwise the TypeTag ("source",
    // "config", "doc", "asset"). The XAML uses Classes on a Path
    // to color them differently if it cares.
    public string GlyphKind => IsFolder ? "folder" : TypeTag;

    public static FileTreeNodeViewModel From(FileTreeNode node)
    {
        var vm = new FileTreeNodeViewModel(
            node.Name,
            node.RelativePath,
            node.IsFolder,
            node.SizeBytes,
            node.TypeTag)
        {
            FileCount = node.FileCount,
        };
        foreach (var child in node.Children)
        {
            vm.Children.Add(From(child));
        }
        return vm;
    }
}
