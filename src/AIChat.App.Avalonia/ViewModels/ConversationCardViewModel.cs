using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the conversation list. The XAML drives the selected
// visual via the .selected class on the sidebar-row style; IsSelected
// is the single source of truth the XAML binds to.
//
// Rename flow: the flyout's "重命名" item binds to StartRenameCommand;
// the XAML swaps a TextBlock for a TextBox bound to EditingTitle.
// Enter / focus-lost commits (calls onTitleChange → parent persists
// + re-sorts the list); Esc cancels and rolls back EditingTitle.
// The onTitleChange callback is supplied by ConversationListViewModel
// so the card doesn't need to know about the repository or the
// domain Conversation it came from.
public sealed partial class ConversationCardViewModel(
    string id,
    string title,
    string detail,
    Func<string, string, Task>? onTitleChange) : ObservableObject
{
    public string Id { get; } = id;
    public string Detail { get; } = detail;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private string title = title;

    // Inline-edit state. EditingTitle mirrors Title while the user
    // is typing so a cancel can roll back without touching Title.
    // IsRenaming drives the XAML swap (TextBlock ↔ TextBox).
    [ObservableProperty]
    private bool isRenaming;

    [ObservableProperty]
    private string editingTitle = title;

    [RelayCommand]
    private void StartRename()
    {
        EditingTitle = Title;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
        EditingTitle = Title;
    }

    [RelayCommand]
    private async Task CommitRenameAsync()
    {
        var trimmed = (EditingTitle ?? string.Empty).Trim();
        IsRenaming = false;
        if (string.IsNullOrEmpty(trimmed) || trimmed == Title)
        {
            EditingTitle = Title;
            return;
        }

        Title = trimmed;
        if (onTitleChange is not null)
        {
            await onTitleChange(Id, trimmed);
        }
    }
}
