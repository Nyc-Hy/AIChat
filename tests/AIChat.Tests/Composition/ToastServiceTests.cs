using AIChat.App.Avalonia.Composition;

namespace AIChat.Tests.Composition;

// Unit tests for the toast surface. The XAML isn't exercised
// here — the contract under test is the queue / promote / drop
// behaviour, which is purely ObservableCollection<ToastItem>
// state + a synchronous UI dispatch.
//
// The constructor takes an Action<Action> dispatcher so we
// don't have to spin up a real Avalonia Dispatcher.UIThread in
// the headless test host. The fake just runs the action inline
// (the tests don't need real cross-thread behaviour — only the
// queue / promote / dismiss ordering does).
public class ToastServiceTests
{
    private static ToastService NewService()
    {
        return new ToastService(action => action());
    }

    [Fact]
    public void Show_Single_AddsToCollection()
    {
        var service = NewService();
        service.Show("hello", ToastLevel.Info);

        Assert.Single(service.Toasts);
        Assert.Equal("hello", service.Toasts[0].Message);
    }

    [Fact]
    public void Show_MultipleUnderCap_AllVisible()
    {
        var service = NewService();
        service.Show("a");
        service.Show("b");

        Assert.Equal(2, service.Toasts.Count);
        Assert.Equal("a", service.Toasts[0].Message);
        Assert.Equal("b", service.Toasts[1].Message);
    }

    [Fact]
    public void Show_Overflow_DoesNotSilentlyDrop_InsteadEnqueues()
    {
        // 1.0.1 fix: the pre-1.0.1 behaviour was to RemoveAt(0)
        // the oldest toast to make room for the 4th, silently
        // dropping the message the user was reading. The new
        // shape keeps every message; the 4th sits in the
        // pending queue and is promoted when one of the
        // visible three dismisses.
        var service = NewService();
        service.Show("1");
        service.Show("2");
        service.Show("3");
        service.Show("4");
        service.Show("5");

        // Visible: the first 3.
        Assert.Equal(3, service.Toasts.Count);
        Assert.Equal("1", service.Toasts[0].Message);
        Assert.Equal("2", service.Toasts[1].Message);
        Assert.Equal("3", service.Toasts[2].Message);

        // Dismiss #1 — the queued #4 must be promoted.
        service.Dismiss(service.Toasts[0]);
        Assert.Equal(3, service.Toasts.Count);
        Assert.DoesNotContain(service.Toasts, t => t.Message == "1");
        Assert.Contains(service.Toasts, t => t.Message == "4");
    }

    [Fact]
    public void Dismiss_PromotesNextPending_AfterEachRemoval()
    {
        var service = NewService();
        service.Show("1");
        service.Show("2");
        service.Show("3");
        service.Show("4");
        service.Show("5");
        service.Show("6");

        Assert.Equal(3, service.Toasts.Count);

        service.Dismiss(service.Toasts[0]); // 1 → 4
        service.Dismiss(service.Toasts[0]); // 2 → 5
        service.Dismiss(service.Toasts[0]); // 3 → 6

        Assert.Equal(3, service.Toasts.Count);
        Assert.Equal(new[] { "4", "5", "6" },
            service.Toasts.Select(t => t.Message).ToArray());
    }

    [Fact]
    public void Dismiss_NonVisibleItem_DoesNotPromote()
    {
        // Dismissing an item that isn't in the visible collection
        // (e.g. its auto-dismiss timer fires after a manual
        // dismiss already removed it) must not promote a
        // duplicate from the queue.
        var service = NewService();
        service.Show("a");
        service.Show("b");
        service.Show("c");
        service.Show("d");

        var first = service.Toasts[0];
        // Remove via the visible API.
        service.Dismiss(first);
        // Now first is no longer in Toasts — another Dismiss(first)
        // is a no-op and must not pull a phantom item out of the
        // queue (the queue head was already promoted by the first
        // Dismiss).
        service.Dismiss(first);

        Assert.Equal(3, service.Toasts.Count);
        Assert.Contains(service.Toasts, t => t.Message == "b");
        Assert.Contains(service.Toasts, t => t.Message == "c");
        Assert.Contains(service.Toasts, t => t.Message == "d");
    }

    [Fact]
    public void Show_AfterDrain_PromotesImmediately()
    {
        // Drain the queue, then show more — they land in the
        // visible window without waiting.
        var service = NewService();
        service.Show("1");
        service.Dismiss(service.Toasts[0]);

        Assert.Empty(service.Toasts);

        service.Show("2");
        service.Show("3");

        Assert.Equal(2, service.Toasts.Count);
        Assert.Equal("2", service.Toasts[0].Message);
        Assert.Equal("3", service.Toasts[1].Message);
    }

    // ---- 1.0.1: click-to-dismiss (ToastItem.Dismiss + OnDismiss) ----

    [Fact]
    public void ToastItem_Dismiss_WithNoCallback_IsNoOp()
    {
        // A ToastItem constructed outside the
        // service (test double, etc.) has no
        // OnDismiss callback. Calling Dismiss
        // on it must not throw — the XAML
        // can still call Dismiss() and the
        // null callback chain just no-ops.
        var item = new ToastItem { Message = "x" };
        item.Dismiss();
    }

    [Fact]
    public void ToastItem_Dismiss_InvokesCallbackWithSelf()
    {
        // The callback is wired to the
        // service's Dismiss(item) so a user
        // click and the auto-dismiss timer
        // race to the same Remove() /
        // PromoteQueued() path. The
        // callback fires with the item
        // itself as the arg so the service
        // can identify which toast to
        // remove.
        var item = new ToastItem
        {
            Message = "x",
            OnDismiss = captured => captured.Dismiss(),
        };
        // The captured lambda is the
        // argument; calling item.Dismiss()
        // would recurse. The test is that
        // the callback fires at all — the
        // callback body here is the
        // "would normally be service.Dismiss"
        // stand-in.
        var fired = false;
        var observed = (ToastItem?)null;
        var item2 = new ToastItem
        {
            Message = "y",
            OnDismiss = captured => { fired = true; observed = captured; }
        };
        item2.Dismiss();
        Assert.True(fired);
        Assert.Same(item2, observed);
    }

    [Fact]
    public void ClickToDismiss_ImmediatelyRemovesTheToast()
    {
        // The XAML click handler routes back
        // through ToastItem.Dismiss, which
        // fires the OnDismiss callback the
        // service set at Show time. That's
        // the same Dismiss() the
        // auto-dismiss timer uses, so a
        // user click and a 3s timeout race
        // to the same Remove() — whichever
        // lands first wins, the second is
        // a no-op. The auto-dismiss timer
        // is the path this test exercises
        // (so the test doesn't have to wait
        // 3s for the timer to fire).
        var service = new ToastService(action => action());
        service.Show("first", ToastLevel.Info);
        service.Show("second", ToastLevel.Warning);
        Assert.Equal(2, service.Toasts.Count);

        // The XAML click handler does the
        // same as below: cast the Border's
        // DataContext to ToastItem and call
        // Dismiss.
        var second = service.Toasts[1];
        second.Dismiss();

        Assert.Single(service.Toasts);
        Assert.Equal("first", service.Toasts[0].Message);
    }

    [Fact]
    public void ClickToDismiss_FreesAVisibleSlot_PromotesTheNextQueuedToast()
    {
        // 1.0.1's FIFO queue means the 4th
        // toast enqueues (not overwrites).
        // When the user clicks the 2nd
        // visible toast to dismiss it, the
        // next queued item should land in
        // the freed slot — the user sees
        // every message they triggered, in
        // order. This is the property
        // click-to-dismiss needs to
        // preserve (it would defeat the
        // purpose of the FIFO if a user
        // click silently broke the queue).
        var service = new ToastService(action => action());
        service.Show("1");
        service.Show("2");
        service.Show("3");
        // The 4th is queued (not visible).
        service.Show("4");
        Assert.Equal(3, service.Toasts.Count);

        // Click-dismiss the 2nd visible
        // toast. The queued "4" should
        // promote into the freed slot —
        // the visible count stays at 3,
        // and the new tail is "4" rather
        // than "1, 3".
        service.Toasts[1].Dismiss();

        Assert.Equal(3, service.Toasts.Count);
        Assert.Equal("1", service.Toasts[0].Message);
        Assert.Equal("3", service.Toasts[1].Message);
        Assert.Equal("4", service.Toasts[2].Message);
    }
}
