using System.Collections.ObjectModel;
using AIChat.Domain.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the conversation activity feed: the message list itself, the
// derived HasConversation flag (drives the empty-state / bubbles swap
// in MainWindow XAML), and the bulk-mutation suppression that keeps
// the empty-state from flashing while a conversation is being loaded.
//
// Extracted from MainWindowViewModel in PR-12 to keep the parent VM
// focused on cross-VM coordination instead of bubble bookkeeping.
public sealed partial class ActivityFeedViewModel : ViewModelBase
{
    // Set during LoadConversation's clear+add loop so the
    // CollectionChanged handler does not flip HasConversation between
    // Clear and the first Add. The flag is recomputed once at the end.
    private bool _suppressHasConversation;

    public ObservableCollection<ActivityItemViewModel> Activity { get; } = [];

    [ObservableProperty]
    private bool hasConversation;

    // 2026-08-06: raised exactly once at the end of LoadConversation
    // (and never from the streaming Add path). The View uses this to
    // scroll the freshly-loaded feed to the bottom at
    // DispatcherPriority.Loaded — late enough that the ItemsControl
    // has finished laying out the bulk-inserted items, so the
    // ScrollToEnd call sees the full extent instead of an intermediate
    // one. Relying on the CollectionChanged.Add branch alone is not
    // enough: each Add posts a ScrollToEnd at Background priority,
    // and the last Add's Post runs before the ItemsControl has re-
    // measured the N added rows, so the final offset is wrong on long
    // conversations (the user lands at the top instead of the bottom).
    public event EventHandler? Loaded;

    public ActivityFeedViewModel()
    {
        Activity.CollectionChanged += (_, _) =>
        {
            if (!_suppressHasConversation)
            {
                HasConversation = Activity.Count > 0;
            }
        };
    }

    public void Add(ActivityItemViewModel item) => Activity.Add(item);

    public void Add(string title, string content, string status)
        => Activity.Add(new ActivityItemViewModel(title, content, status));

    public void Clear() => Activity.Clear();

    // v1 bug B-4 fix: clear + bulk-insert without firing HasConversation
    // changes in between. The CollectionChanged handler flips
    // HasConversation for every Add; if we let it run mid-loop the empty
    // state and the conversation panel swap multiple times in one frame.
    // Suppress notifications for the bulk update, then recompute once.
    //
    // 2026-08-06: raises Loaded exactly once at the end so the View
    // can scroll to the bottom at DispatcherPriority.Loaded (after
    // layout) instead of racing the per-Add ScrollToEnd posts. A null
    // conversation still raises Loaded so subscribers (the View) can
    // reset their own state in lockstep with the empty feed.
    public void LoadConversation(ChatSession? conversation)
    {
        if (conversation is null)
        {
            Clear();
            Loaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        _suppressHasConversation = true;
        try
        {
            Activity.Clear();
            foreach (var message in conversation.Messages.OrderBy(message => message.CreatedAt))
            {
                var title = message.Role == ChatRole.User ? "你" : "AIChat";
                var status = message.CreatedAt.ToLocalTime().ToString("HH:mm");
                Activity.Add(new ActivityItemViewModel(title, message.Content, status));
            }

            if (Activity.Count == 0)
            {
                Activity.Add(new ActivityItemViewModel("AIChat", "这个对话还没有消息。", "空"));
            }
        }
        finally
        {
            _suppressHasConversation = false;
        }
        HasConversation = Activity.Count > 0;
        Loaded?.Invoke(this, EventArgs.Empty);
    }
}
