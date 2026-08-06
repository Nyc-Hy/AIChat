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
// item lands. It is reset to zero from three call sites: the
// pill's click handler (jump to bottom), the ScrollChanged
// listener when the user scrolls back to the bottom on their
// own, and the host's OnConversationSelected handler (when the
// user picks a different conversation — the new feed loads
// at offset 0 so the previous counter would otherwise leak).
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

    // 2026-08-05: tracks whether the
    // user has scrolled away from the
    // top of the conversation. Drives
    // the visibility of the "↑ 顶部"
    // affordance at the top of the
    // panel — the inverse of the
    // "↓ N 条新消息" pill at the bottom.
    // Long conversations (50+ turns)
    // are hard to navigate without a
    // quick "back to the beginning"
    // button once the user has read
    // deep into the middle.
    //
    // The flag is bumped by the
    // ScrollChanged handler in
    // MainWindow when the offset > 0
    // (i.e. the user is not parked at
    // the very top) and cleared when
    // they scroll back to the start.
    // The XAML IsVisible binding
    // picks it up directly — no
    // intermediary host property
    // needed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScrollToTop))]
    private bool isScrolledFromTop;

    public bool CanScrollToTop => IsScrolledFromTop;
}
