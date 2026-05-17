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
    public string BenchmarkSummary => Run.BenchmarkSummary;
    public string AcceptanceStatusText => Run.AcceptanceStatusText;
    public string AcceptanceNote => Run.AcceptanceNote;
    public bool HasAcceptanceNote => Run.HasAcceptanceNote;
    public bool NeedsChanges => Run.AcceptanceStatus == AIChat.Domain.Chat.AgentRunAcceptanceStatus.NeedsChanges;
    public string AcceptanceSummary => HasAcceptanceNote
        ? $"{AcceptanceStatusText} · {AcceptanceNote}"
        : AcceptanceStatusText;
    public bool CanRetry => Run.CanRetry;
    public bool CanContinue => Run.CanContinue;
    public bool CanResume => Run.CanResume;
    public bool HasContinuation => Run.HasContinuation;
    public string ContinuedFromRunText => Run.ContinuedFromRunText;
}
