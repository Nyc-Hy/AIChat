using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Avalonia;

// 1.0.6: switching to a different
// conversation must reset the
// scroll-position signals so a
// "↓ N 条新消息" pill (counting
// unread chunks from the previous
// conversation) or a "↑ 顶部"
// button (indicating the user
// had scrolled away from the
// start of the previous
// conversation) doesn't leak onto
// the freshly-loaded activity
// feed. The two signals are
// independent (one counts from
// the bottom, one is a bool at
// the top) but both belong to
// the conversation the user just
// left.
public class ConversationSwitchScrollResetTests
{
    [Fact]
    public void SwitchConversation_ClearsUnseenCounter()
    {
        // Simulate a user who was
        // scrolled up while a long
        // stream was landing: the
        // unseen counter goes to 3
        // (the pill would read "↓ 3 条
        // 新消息"). They then pick a
        // different conversation
        // from the list. The pill
        // must drop to 0 — the new
        // conversation has nothing
        // "below the visible area" to
        // surface.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        viewModel.MessageScroll.IncrementUnseenMessageCount();
        viewModel.MessageScroll.IncrementUnseenMessageCount();
        viewModel.MessageScroll.IncrementUnseenMessageCount();
        Assert.Equal(3, viewModel.MessageScroll.UnseenMessageCount);
        Assert.True(viewModel.MessageScroll.HasUnseenMessages);

        // Drive a session through
        // the conversation list so
        // SelectConversation fires
        // the real ConversationSelected
        // event the host subscribes
        // to. The host's handler runs
        // synchronously before
        // SelectConversation returns,
        // so the MessageScroll is
        // already cleared by the time
        // we re-read.
        var conversationList = host.GetRequiredService<ConversationListViewModel>();
        var session = new Project
        {
            Id = "session-1",
            WorkspaceId = "test-project",
            Title = "first",
            UpdatedAt = DateTimeOffset.Now
        };
        conversationList.Refresh(project: null, sessions: [session], preferredConversationId: null);
        conversationList.SelectConversation("session-1");

        Assert.Equal(0, viewModel.MessageScroll.UnseenMessageCount);
        Assert.False(viewModel.MessageScroll.HasUnseenMessages);
    }

    [Fact]
    public void SwitchConversation_ResetsIsScrolledFromTop()
    {
        // The "↑ 顶部" button is
        // visible when IsScrolledFromTop
        // is true. A user who
        // scrolled into the middle of
        // conversation A then switched
        // to conversation B would
        // otherwise still see the
        // button on the new feed
        // (which loads at the top).
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        viewModel.MessageScroll.IsScrolledFromTop = true;
        Assert.True(viewModel.MessageScroll.CanScrollToTop);

        var conversationList = host.GetRequiredService<ConversationListViewModel>();
        var session = new Project
        {
            Id = "session-1",
            WorkspaceId = "test-project",
            Title = "first",
            UpdatedAt = DateTimeOffset.Now
        };
        conversationList.Refresh(project: null, sessions: [session], preferredConversationId: null);
        conversationList.SelectConversation("session-1");

        Assert.False(viewModel.MessageScroll.IsScrolledFromTop);
        Assert.False(viewModel.MessageScroll.CanScrollToTop);
    }
}
