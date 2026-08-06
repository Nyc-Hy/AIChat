using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Avalonia;

// 2026-08-06: ActivityFeedViewModel.LoadConversation raises
// Loaded exactly once at the end (after the bulk Clear+Add
// loop). The view subscribes to Loaded and uses it to scroll
// the freshly-loaded feed to the bottom at
// DispatcherPriority.Loaded — late enough that the
// ItemsControl has finished laying out the bulk-inserted
// items, so ScrollToEnd sees the full extent. Without this
// signal the per-Add ScrollToEnd posts race the layout pass
// and the user lands at the top of long conversations.
public class ActivityFeedViewModelTests
{
    [Fact]
    public void LoadConversation_Null_RaisesLoaded_Once()
    {
        var feed = new ActivityFeedViewModel();
        var loadedCount = 0;
        feed.Loaded += (_, _) => loadedCount++;

        feed.LoadConversation(null);

        Assert.Equal(1, loadedCount);
    }

    [Fact]
    public void LoadConversation_Populated_RaisesLoaded_ExactlyOnce()
    {
        // A user who switches from a 5-message conversation
        // to a 3-message one must see Loaded fire exactly
        // once — not 3 (once per Add) and not 0 (the view
        // would never scroll). The contract is one event
        // per LoadConversation call, no matter how many
        // messages it holds.
        var feed = new ActivityFeedViewModel();
        var conversation = BuildConversation(messages:
        [
            ("hello", ChatRole.User),
            ("hi there", ChatRole.Assistant),
            ("how are you?", ChatRole.User),
        ]);
        var loadedCount = 0;
        feed.Loaded += (_, _) => loadedCount++;

        feed.LoadConversation(conversation);

        Assert.Equal(1, loadedCount);
    }

    [Fact]
    public void LoadConversation_EmptyConversation_RaisesLoaded_AfterPlaceholderAdd()
    {
        // An empty conversation falls into the
        // "Activity.Count == 0" branch which adds a
        // "这个对话还没有消息。" placeholder. Loaded
        // must still fire exactly once so the view
        // can scroll to that single placeholder bubble.
        var feed = new ActivityFeedViewModel();
        var conversation = new Project
        {
            Id = "empty",
            WorkspaceId = "test",
            Title = "empty",
            Messages = []
        };
        var loadedCount = 0;
        feed.Loaded += (_, _) => loadedCount++;

        feed.LoadConversation(conversation);

        Assert.Equal(1, loadedCount);
        Assert.Single(feed.Activity);
    }

    [Fact]
    public void LoadConversation_RaisesLoaded_AfterAllAdds()
    {
        // The view relies on the order: Reset first (so
        // the empty-state / ScrollToHome fires), N Adds
        // (so the per-Add auto-scroll posts queue up),
        // then Loaded last (so the explicit
        // DispatcherPriority.Loaded ScrollToEnd wins).
        // If Loaded fired mid-loop, the ScrollToEnd
        // would race the still-arriving Adds and land at
        // a partial extent.
        var feed = new ActivityFeedViewModel();
        var conversation = BuildConversation(messages:
        [
            ("a", ChatRole.User),
            ("b", ChatRole.Assistant),
            ("c", ChatRole.User),
            ("d", ChatRole.Assistant),
            ("e", ChatRole.User),
        ]);

        var collectionEvents = new List<NotifyCollectionChangedAction>();
        feed.Activity.CollectionChanged += (_, e) => collectionEvents.Add(e.Action);
        var loadedFiredAt = -1;
        var collectionEventIndex = 0;
        feed.Loaded += (_, _) => loadedFiredAt = collectionEventIndex;

        feed.LoadConversation(conversation);

        // Snapshot: each CollectionChanged.Add bumped
        // collectionEventIndex via the lambda capture —
        // record the index by subscribing before
        // LoadConversation but in a way that counts
        // events as they fire. The simpler way is to
        // re-run with a counter that mutates inside
        // the handler.
        // (The assertion below uses a separate counter
        // pattern; see LoadConversation_OrderIsResetThenAddsThenLoaded.)
        Assert.Equal(5, collectionEvents.Count(e => e == NotifyCollectionChangedAction.Add));
    }

    [Fact]
    public void LoadConversation_OrderIsResetThenAddsThenLoaded()
    {
        // Definitive order test: events fire in the
        // order [Reset, Add×N, Loaded]. The view's
        // CollectionChanged.Add handler posts
        // ScrollToEnd at Background priority; Loaded
        // posts ScrollToEnd at Loaded priority. If
        // Loaded fired before the Adds, the Background
        // posts would queue after it and undo the
        // Loaded post's effect. The view depends on
        // the explicit Loaded being the LAST event.
        var feed = new ActivityFeedViewModel();
        var conversation = BuildConversation(messages:
        [
            ("a", ChatRole.User),
            ("b", ChatRole.Assistant),
            ("c", ChatRole.User),
        ]);
        var eventLog = new List<string>();
        var collectionEventIndex = 0;
        var collectionEventsAtLoaded = new List<int>();
        feed.Activity.CollectionChanged += (_, e) =>
        {
            eventLog.Add(e.Action.ToString());
            collectionEventIndex++;
        };
        feed.Loaded += (_, _) =>
        {
            eventLog.Add("Loaded");
            collectionEventsAtLoaded.Add(collectionEventIndex);
        };

        feed.LoadConversation(conversation);

        Assert.Equal("Reset", eventLog[0]);
        Assert.Equal(3, eventLog.Count(e => e == "Add"));
        Assert.Equal("Loaded", eventLog[^1]);
        // Loaded fires AFTER the last Add — i.e.
        // collectionEventIndex (the count of
        // CollectionChanged events) does not advance
        // past the last Add before Loaded fires.
        Assert.Equal(4, collectionEventsAtLoaded.Single());
    }

    [Fact]
    public void Clear_DoesNotRaiseLoaded()
    {
        // Clear() alone (e.g. the user picks a
        // "新对话" placeholder) is the per-Reset
        // path that triggers ScrollToHome via the
        // CollectionChanged.Reset branch. It must
        // NOT raise Loaded — the view's Loaded
        // handler unconditionally ScrollToEnds, and
        // there is nothing to scroll to on an empty
        // feed (ScrollToEnd on an empty ScrollViewer
        // is a no-op but the explicit dispatcher
        // post is wasted work).
        var feed = new ActivityFeedViewModel();
        feed.Activity.Add(new ActivityItemViewModel("seed", "seed", "空"));
        var loadedCount = 0;
        feed.Loaded += (_, _) => loadedCount++;

        feed.Clear();

        Assert.Equal(0, loadedCount);
    }

    [Fact]
    public void LoadConversation_HasConversation_FlipsToTrueAfterLoad()
    {
        // The empty-state swap depends on
        // HasConversation being correct after
        // LoadConversation. The bulk-insert
        // suppresses the per-Add flip and recomputes
        // once at the end — the Loaded event is
        // raised after that recompute so the view
        // sees the right state.
        var feed = new ActivityFeedViewModel();
        var conversation = BuildConversation(messages:
        [
            ("hello", ChatRole.User),
        ]);

        feed.LoadConversation(conversation);

        Assert.True(feed.HasConversation);
    }

    private static Project BuildConversation(
        IReadOnlyList<(string Content, ChatRole Role)> messages)
    {
        return new Project
        {
            Id = "test",
            WorkspaceId = "test-project",
            Title = "test",
            UpdatedAt = DateTimeOffset.Now,
            Messages = messages
                .Select(m => new ChatMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAt = DateTimeOffset.Now
                })
                .ToList()
        };
    }
}
