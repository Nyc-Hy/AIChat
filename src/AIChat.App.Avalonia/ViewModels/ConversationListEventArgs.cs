using AIChat.Domain.Chat;

namespace AIChat.App.Avalonia.ViewModels;

// Raised by ConversationListViewModel whenever the user picks a different
// conversation (or the "new" placeholder). The parent view-model reacts by
// either loading the conversation's messages into the activity feed or
// showing the "new conversation" prompt.
public sealed class ConversationSelectedEventArgs : EventArgs
{
    // Null when the selection is the "new" placeholder.
    public ChatSession? Conversation { get; init; }
    public required string StatusMessage { get; init; }
}
