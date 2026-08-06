using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// 2026-08-05: tests for the new
// IsScrolledFromTop / CanScrollToTop
// flags that drive the "↑ 顶部" button
// at the top of the conversation
// panel. The "↓ N 条新消息" pill at
// the bottom already has its own
// counter; the top button only needs
// a bool (no count — "1 unread turn
// at the top" is the same affordance
// as "47 unread turns at the top",
// the user wants to scroll there
// regardless).
public class MessageScrollStateTests
{
    [Fact]
    public void IsScrolledFromTop_False_ByDefault()
    {
        // Fresh state: the user is
        // parked at offset 0, the
        // button must be hidden so
        // a 3-line conversation
        // doesn't carry a permanent
        // "↑ 顶部" affordance they
        // can never use.
        var state = new MessageScrollState();
        Assert.False(state.IsScrolledFromTop);
        Assert.False(state.CanScrollToTop);
    }

    [Fact]
    public void IsScrolledFromTop_True_AfterScroll()
    {
        // The MainWindow ScrollChanged
        // handler sets this to true
        // when offsetY > 8. We can't
        // pump Avalonia's ScrollViewer
        // in a unit test, but the
        // setter itself is the surface
        // — the handler just writes
        // to it on each event.
        var state = new MessageScrollState();
        state.IsScrolledFromTop = true;
        Assert.True(state.IsScrolledFromTop);
        Assert.True(state.CanScrollToTop);
    }

    [Fact]
    public void IsScrolledFromTop_ReRaisesCanScrollToTop()
    {
        // The XAML's IsVisible binding
        // on the "↑ 顶部" button is
        // bound to CanScrollToTop
        // (the derived bool). The
        // derived property must
        // re-raise when the source
        // bool flips, or the button
        // stays hidden the first time
        // the user scrolls down on a
        // freshly-loaded conversation.
        var state = new MessageScrollState();
        var reRaised = new System.Collections.Generic.HashSet<string>();
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null) reRaised.Add(e.PropertyName);
        };
        state.IsScrolledFromTop = true;
        Assert.Contains(nameof(MessageScrollState.CanScrollToTop), reRaised);
    }

    [Fact]
    public void UnseenCounter_Still_Works_After_ScrollToTop_Addition()
    {
        // Defensive: adding the
        // IsScrolledFromTop property
        // must not break the existing
        // "↓ N 条新消息" counter
        // semantics. A user who has
        // unread messages AND has
        // scrolled away from the top
        // should see BOTH the bottom
        // pill and the top button —
        // the two are independent
        // (different directions, different
        // intentions).
        var state = new MessageScrollState();
        state.IncrementUnseenMessageCount();
        state.IncrementUnseenMessageCount();
        state.IsScrolledFromTop = true;

        Assert.True(state.HasUnseenMessages);
        Assert.Equal("↓ 2 条新消息", state.UnseenMessageLabel);
        Assert.True(state.CanScrollToTop);
    }
}
