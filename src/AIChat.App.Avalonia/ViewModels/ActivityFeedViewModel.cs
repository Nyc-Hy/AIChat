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
    public void LoadConversation(ChatSession? conversation)
    {
        if (conversation is null)
        {
            Clear();
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
    }
}
