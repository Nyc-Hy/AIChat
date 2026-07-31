using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the conversation list. The XAML drives the selected
// visual via the .selected class on the sidebar-row style; IsSelected
// is the single source of truth the XAML binds to. (Previously this
// class also exposed a Background hex string + the parent assigned
// "#FFFFFF" / "#FFFFFF00" in code, but nothing bound it — the
// class-based selector wins the visual race, and the strings were
// dead. Removed in the dead-code cleanup pass.)
public sealed partial class ConversationCardViewModel(string id, string title, string detail) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Detail { get; } = detail;

    [ObservableProperty]
    private bool isSelected;
}
