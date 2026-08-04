using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// 1.0.1: the "最近" sidebar item. Carries
// the conversation title (for display) and
// the routing key (the underlying
// ConversationCardViewModel.Id the click
// handler uses to drive the existing
// SelectedConversationCard → SelectConversation
// path). Tests cover the small surface —
// the broader behaviour is covered by
// ConversationListViewModelTests (which
// exercises the SelectConversation + event
// raise that RecentItem_OnClick reuses).
public class RecentItemViewModelTests
{
    [Fact]
    public void Ctor_StoresTitleAndConversationIdAndUpdatedAtDisplay()
    {
        var item = new RecentItemViewModel("hello", "abc123", "8月4日 14:30");
        Assert.Equal("hello", item.Title);
        Assert.Equal("abc123", item.ConversationId);
        Assert.Equal("8月4日 14:30", item.UpdatedAtDisplay);
    }
}
