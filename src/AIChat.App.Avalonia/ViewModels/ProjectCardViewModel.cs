using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the sidebar's 项目 list. Id is the project record id
// (used by the XAML Tag + the SelectProjectAsync path), Path is the
// on-disk root, Name is what the user sees. IsSelected drives the
// .selected class on the sidebar-row style (the visual state, not
// a Background hex string — see the cleanup in the dead-code
// commit that also touched ConversationCardViewModel).
public sealed partial class ProjectCardViewModel(string id, string name, string path) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Path { get; } = path;

    [ObservableProperty]
    private bool isSelected;
}
