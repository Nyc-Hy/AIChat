namespace AIChat.App.ViewModels;

public sealed class AgentRunHistoryItemViewModel : ObservableObject
{
    public required ConversationViewModel Conversation { get; init; }
    public required AgentRunViewModel Run { get; init; }

    public string Id => Run.Id;
    public string ConversationTitle => Conversation.Title;
    public string Goal => Run.Goal;
    public string ShortGoal => Run.ShortGoal;
    public string StatusText => Run.StatusText;
    public string PhaseText => Run.PhaseText;
    public string StartedText => Run.StartedText;
    public string Summary => Run.Summary;
    public bool CanRetry => Run.CanRetry;
}
