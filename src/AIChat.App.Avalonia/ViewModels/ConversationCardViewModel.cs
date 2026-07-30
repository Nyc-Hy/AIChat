using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the conversation list. Background is exposed as a hex string
// (not IBrush) so the view-model can be unit-tested without spinning up
// Avalonia's graphics stack — Avalonia's binding layer auto-converts the
// string to a brush when the XAML binds it to a Background property.
//
// IsSelected is driven by ConversationListViewModel when a conversation is
// chosen; the XAML uses it to apply the .selected class on the sidebar row.
public sealed partial class ConversationCardViewModel(string id, string title, string detail) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Detail { get; } = detail;

    [ObservableProperty]
    private string background = "#FFFFFF00";

    [ObservableProperty]
    private bool isSelected;
}
