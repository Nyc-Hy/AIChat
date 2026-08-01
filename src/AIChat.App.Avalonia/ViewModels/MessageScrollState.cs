using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// Tracks the "the user is scrolled up while new activity arrives"
// counter that drives the floating "↓ N 条新消息" pill at the
// bottom of the conversation panel. Extracted from
// MainWindowViewModel so the host VM stops carrying scroll-state
// the conversation view + auto-scroll handler own.
//
// The counter is bumped from the ScrollChanged handler in
// MainWindow when the user has scrolled up and a new activity
// item lands. The pill's click handler, the same ScrollChanged
// listener when the user scrolls back to the bottom, and the
// ConversationList's conversation-switch / Clear flow all reset
// it to zero.
public sealed partial class MessageScrollState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnseenMessages))]
    [NotifyPropertyChangedFor(nameof(UnseenMessageLabel))]
    private int unseenMessageCount;

    public bool HasUnseenMessages => UnseenMessageCount > 0;

    public string UnseenMessageLabel => UnseenMessageCount <= 1
        ? "↓ 新消息"
        : $"↓ {UnseenMessageCount} 条新消息";

    public void IncrementUnseenMessageCount() => UnseenMessageCount++;
    public void ClearUnseenMessageCount() => UnseenMessageCount = 0;
}
