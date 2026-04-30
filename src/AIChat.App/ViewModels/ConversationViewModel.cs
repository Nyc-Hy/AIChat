using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

// UI projection of a Conversation. It owns observable collections because WPF
// list controls update automatically when ObservableCollection changes.
public sealed class ConversationViewModel : ObservableObject
{
    private bool _isSelected;
    private string _searchSnippet = "";

    public ConversationViewModel(Conversation conversation)
    {
        Conversation = conversation;
        Messages = new ObservableCollection<ChatMessageViewModel>(
            conversation.Messages.Select(message => new ChatMessageViewModel(
                message,
                conversation.AgentRuns.FirstOrDefault(run => run.Id == message.AgentRunId))));
        CallDetails = new ObservableCollection<LlmCallDetailViewModel>(
            conversation.CallDetails
                .OrderByDescending(detail => detail.CreatedAt)
                .Select(detail => new LlmCallDetailViewModel(detail)));
    }

    public Conversation Conversation { get; }
    public ObservableCollection<ChatMessageViewModel> Messages { get; }
    public ObservableCollection<LlmCallDetailViewModel> CallDetails { get; }

    public string Id => Conversation.Id;
    public string Title => Conversation.Title;
    public string UpdatedText => Conversation.UpdatedAt.ToLocalTime().ToString("M/d");
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

        Conversation.Messages.Add(message);
        Messages.Add(new ChatMessageViewModel(message));
        Conversation.UpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(UpdatedText));
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

        var message = Messages.FirstOrDefault(item =>
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
