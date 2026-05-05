using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

// UI projection of a Conversation. It owns observable collections because WPF
// list controls update automatically when ObservableCollection changes.
public sealed class ConversationViewModel : ObservableObject
{
    private const int InitialRenderWindowSize = 5;
    private const int RenderWindowBatchSize = 80;

    private bool _isSelected;
    private string _searchSnippet = "";
    private ObservableCollection<ChatMessageViewModel>? _messages;
    private ObservableCollection<LlmCallDetailViewModel>? _callDetails;
    private int _renderedMessageStartIndex;

    public ConversationViewModel(Conversation conversation)
    {
        Conversation = conversation;
    }

    public Conversation Conversation { get; }
    public ObservableCollection<ChatMessageViewModel> Messages => _messages ??= BuildMessages();
    public ObservableCollection<LlmCallDetailViewModel> CallDetails => _callDetails ??= BuildCallDetails();

    public string Id => Conversation.Id;
    public string Title => Conversation.Title;
    public string UpdatedText => Conversation.UpdatedAt.ToLocalTime().ToString("M/d");
    public int MessageCount => Conversation.Messages.Count;
    public bool HasHiddenMessages => HiddenMessageCount > 0;
    public int HiddenMessageCount => _messages is null
        ? Math.Max(0, Conversation.Messages.Count - InitialRenderWindowSize)
        : _renderedMessageStartIndex;
    public string LoadEarlierMessagesText => HiddenMessageCount <= 0
        ? "已显示全部消息"
        : $"加载更早的 {Math.Min(RenderWindowBatchSize, HiddenMessageCount)} 条消息";
    public string SearchSnippet
    {
        get => _searchSnippet;
        private set => SetProperty(ref _searchSnippet, value);
    }

    public bool HasSearchSnippet => !string.IsNullOrWhiteSpace(SearchSnippet);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void AddMessage(ChatMessage message)
    {
        // The first user message becomes the conversation title, which keeps the
        // MVP usable without a separate title-generation model call.
        if (message.Role == ChatRole.User &&
            Conversation.Messages.Count == 0 &&
            Conversation.Title == "新对话")
        {
            Conversation.Title = CreateTitle(message.Content);
            OnPropertyChanged(nameof(Title));
        }

        var messagesLoaded = _messages is not null;
        Conversation.Messages.Add(message);
        if (messagesLoaded)
        {
            Messages.Add(new ChatMessageViewModel(message));
        }

        Conversation.UpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(HiddenMessageCount));
        OnPropertyChanged(nameof(LoadEarlierMessagesText));
    }

    public void LoadEarlierMessages()
    {
        if (!HasHiddenMessages)
        {
            return;
        }

        var newStartIndex = Math.Max(0, _renderedMessageStartIndex - RenderWindowBatchSize);
        var runsById = Conversation.AgentRuns.ToDictionary(run => run.Id, StringComparer.Ordinal);
        for (var index = _renderedMessageStartIndex - 1; index >= newStartIndex; index--)
        {
            Messages.Insert(0, CreateMessageViewModel(Conversation.Messages[index], runsById));
        }

        _renderedMessageStartIndex = newStartIndex;
        OnPropertyChanged(nameof(HasHiddenMessages));
        OnPropertyChanged(nameof(HiddenMessageCount));
        OnPropertyChanged(nameof(LoadEarlierMessagesText));
    }

    public AgentRun AddAgentRun(ChatMessageViewModel assistantMessage, string userMessageId, string goal)
    {
        var run = new AgentRun
        {
            ConversationId = Conversation.Id,
            UserMessageId = userMessageId,
            AssistantMessageId = assistantMessage.Message.Id,
            Goal = goal,
            StartedAt = DateTimeOffset.Now
        };
        Conversation.AgentRuns.Add(run);
        assistantMessage.AttachAgentRun(run);
        Conversation.UpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(UpdatedText));
        return run;
    }

    public bool ApplySearch(string searchText)
    {
        var normalized = searchText.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            SetSearchSnippet("");
            return true;
        }

        if (Title.Contains(normalized, StringComparison.OrdinalIgnoreCase))
        {
            SetSearchSnippet($"标题匹配：{Title}");
            return true;
        }

        var message = Conversation.Messages.FirstOrDefault(item =>
            item.Content.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        if (message is null)
        {
            SetSearchSnippet("");
            return false;
        }

        SetSearchSnippet(CreateSnippet(message.Content, normalized));
        return true;
    }

    private void SetSearchSnippet(string value)
    {
        if (SetProperty(ref _searchSnippet, value, nameof(SearchSnippet)))
        {
            OnPropertyChanged(nameof(HasSearchSnippet));
        }
    }

    public void AddCallDetail(LlmCallDetail detail)
    {
        // Call details are newest-first in the inspector, while the persisted
        // domain list simply records all details.
        Conversation.CallDetails.Add(detail);
        CallDetails.Insert(0, new LlmCallDetailViewModel(detail));
        Conversation.UpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(UpdatedText));
    }

    public void RefreshCallDetail(LlmCallDetail detail)
    {
        if (_callDetails is null)
        {
            return;
        }

        var index = CallDetails
            .Select((item, itemIndex) => new { item, itemIndex })
            .FirstOrDefault(entry => entry.item.Detail.Id == detail.Id)
            ?.itemIndex;

        if (index is null)
        {
            return;
        }

        CallDetails[index.Value] = new LlmCallDetailViewModel(detail);
    }

    public void Rename(string title)
    {
        var normalized = title.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        Conversation.Title = normalized;
        Conversation.UpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(UpdatedText));
    }

    private static string CreateTitle(string content)
    {
        var normalized = content.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 18 ? normalized : $"{normalized[..18]}...";
    }

    private ObservableCollection<ChatMessageViewModel> BuildMessages()
    {
        var runsById = Conversation.AgentRuns.ToDictionary(run => run.Id, StringComparer.Ordinal);
        _renderedMessageStartIndex = Math.Max(0, Conversation.Messages.Count - InitialRenderWindowSize);
        return new ObservableCollection<ChatMessageViewModel>(
            Conversation.Messages
                .Skip(_renderedMessageStartIndex)
                .Select(message => CreateMessageViewModel(message, runsById)));
    }

    private static ChatMessageViewModel CreateMessageViewModel(
        ChatMessage message,
        IReadOnlyDictionary<string, AgentRun> runsById)
    {
        runsById.TryGetValue(message.AgentRunId, out var run);
        return new ChatMessageViewModel(message, run, includeDetails: false);
    }

    private ObservableCollection<LlmCallDetailViewModel> BuildCallDetails()
    {
        return new ObservableCollection<LlmCallDetailViewModel>(
            Conversation.CallDetails
                .OrderByDescending(detail => detail.CreatedAt)
                .Select(detail => new LlmCallDetailViewModel(detail)));
    }

    private static string CreateSnippet(string content, string searchText)
    {
        // Keep search results compact so the sidebar stays scannable.
        var normalized = content.ReplaceLineEndings(" ").Trim();
        var index = normalized.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return normalized.Length <= 42 ? normalized : $"{normalized[..42]}...";
        }

        var start = Math.Max(0, index - 16);
        var length = Math.Min(normalized.Length - start, searchText.Length + 32);
        var prefix = start > 0 ? "..." : "";
        var suffix = start + length < normalized.Length ? "..." : "";
        return $"命中：{prefix}{normalized.Substring(start, length)}{suffix}";
    }
}
