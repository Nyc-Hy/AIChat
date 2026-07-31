using System.Collections.ObjectModel;
using AIChat.Abstractions.Persistence;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the "recent conversations" list and the "currently selected
// conversation" state. PR-4 scope: pure extraction from MainWindowViewModel.
//
// The selected Conversation (a Domain type) is exposed through the
// ConversationSelected event; the activity feed lives on the parent and
// is updated in response. The currently-selected conversation card is
// exposed as SelectedConversationCard for XAML binding.
public sealed partial class ConversationListViewModel : ViewModelBase
{
    private const string NewConversationId = "new";

    private readonly IAppRepository _repository;
    private bool _isApplyingConversationSelection;
    private ProjectWorkspace? _currentProject;

    [ObservableProperty]
    private ConversationCardViewModel? selectedConversationCard;

    public ObservableCollection<ConversationCardViewModel> Conversations { get; } = [];

    public event EventHandler<ConversationSelectedEventArgs>? ConversationSelected;

    public ConversationListViewModel(IAppRepository repository)
    {
        _repository = repository;
    }

    // Replaces the conversation list with the project's recent
    // conversations. If the project is null or has no conversations,
    // a single "new" placeholder card is shown. Raises
    // ConversationSelected so the parent can update the activity feed.
    public void Refresh(ProjectWorkspace? project, string? preferredConversationId = null)
    {
        _currentProject = project;
        Conversations.Clear();

        if (project is null || project.Conversations.Count == 0)
        {
            Conversations.Add(new ConversationCardViewModel(NewConversationId, "新任务", "暂无历史对话"));
            SetSelectedConversation(Conversations[0]);
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        var sorted = project.Conversations
                     .OrderByDescending(conversation => conversation.UpdatedAt)
                     .Take(8)
                     .ToList();
        foreach (var conversation in sorted)
        {
            Conversations.Add(new ConversationCardViewModel(
                conversation.Id,
                string.IsNullOrWhiteSpace(conversation.Title) ? "未命名任务" : conversation.Title,
                conversation.UpdatedAt.ToLocalTime().ToString("M月d日 HH:mm")));
        }

        var selectedCard = Conversations.FirstOrDefault(item => item.Id == preferredConversationId)
                           ?? Conversations.FirstOrDefault();
        SetSelectedConversation(selectedCard);

        var selectedConversation = project.Conversations.FirstOrDefault(item =>
            string.Equals(item.Id, selectedCard?.Id, StringComparison.OrdinalIgnoreCase));
        ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
        {
            Conversation = selectedConversation,
            StatusMessage = selectedConversation is null
                ? "已打开新对话。"
                : $"已打开对话：{selectedConversation.Title}"
        });
    }

    // Public so the view code-behind can call it via MainWindowViewModel
    // passthrough. Selects the conversation with the given id, or the
    // "new" placeholder if id is "new" or unknown. Matches the original
    // behaviour: when the project has real conversations, the list does
    // not contain a "new" card, so clicking "new" or an unknown id does
    // not change the list selection — it just raises the event so the
    // parent can show the "new conversation" prompt.
    [RelayCommand]
    public void SelectConversation(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || _currentProject is null)
        {
            SetSelectedConversation(Conversations.FirstOrDefault(item => item.Id == NewConversationId));
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        if (conversationId == NewConversationId)
        {
            // The "new" card only exists when the project is null or empty.
            // If we get here with a real project, the list selection stays
            // where it was — only the activity feed switches to the
            // "new conversation" prompt via the event.
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        var conversation = _currentProject.Conversations.FirstOrDefault(item =>
            string.Equals(item.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (conversation is null)
        {
            // Unknown id: same as "new" — list selection stays, only the
            // activity feed changes.
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        var card = Conversations.FirstOrDefault(item => item.Id == conversation.Id);
        SetSelectedConversation(card);
        ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
        {
            Conversation = conversation,
            StatusMessage = $"已打开对话：{conversation.Title}"
        });
    }

    private void SetSelectedConversation(ConversationCardViewModel? conversation)
    {
        _isApplyingConversationSelection = true;
        foreach (var item in Conversations)
        {
            item.IsSelected = item.Id == conversation?.Id;
        }

        SelectedConversationCard = conversation;
        _isApplyingConversationSelection = false;
    }

    partial void OnSelectedConversationCardChanged(ConversationCardViewModel? value)
    {
        if (_isApplyingConversationSelection || value is null)
        {
            return;
        }

        SelectConversation(value.Id);
    }

    // Removes the conversation with the given id from the current
    // project. The activity feed and conversation list both
    // refresh — the conversation list drops the row, the activity
    // feed switches to a fresh "new conversation" prompt via
    // ConversationSelected.
    //
    // The project JSON is the source of truth; AgentRunnerViewModel
    // re-reads from the repo on the next SendTaskCommand. Any
    // pending run on this conversation would already be over by
    // the time the user reaches for the right-click menu, so we
    // don't worry about mid-run cancellation.
    [RelayCommand]
    public async Task RemoveConversationAsync(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) ||
            conversationId == NewConversationId ||
            _currentProject is null)
        {
            return;
        }

        var target = _currentProject.Conversations.FirstOrDefault(conversation =>
            string.Equals(conversation.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _currentProject.Conversations.Remove(target);

        // Save the updated project list back to the repo. Mirrors
        // the pattern AgentRunnerViewModel.SaveProjectsAsync uses
        // after a run lands a memory update.
        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var index = projects.FindIndex(project => project.Id == _currentProject.Id);
        if (index >= 0)
        {
            projects[index] = _currentProject;
        }
        else
        {
            projects.Add(_currentProject);
        }
        await _repository.SaveProjectsAsync(projects);

        // Refresh the list so the deleted row disappears, then
        // re-emit ConversationSelected with null so the host's
        // activity feed switches to the "new conversation" prompt.
        Refresh(_currentProject);
        ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
        {
            Conversation = null,
            StatusMessage = $"已删除对话：{target.Title}"
        });
    }
}
