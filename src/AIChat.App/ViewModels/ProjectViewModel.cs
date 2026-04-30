using System.Collections.ObjectModel;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.App.ViewModels;

// UI wrapper for a project workspace. VisibleConversations can be filtered
// without removing the real conversation list.
public sealed class ProjectViewModel : ObservableObject
{
    private bool _isSelected;

    public ProjectViewModel(ProjectWorkspace project)
    {
        Project = project;
        Conversations = new ObservableCollection<ConversationViewModel>(
            project.Conversations.Select(conversation => new ConversationViewModel(conversation)));
        VisibleConversations = new ObservableCollection<ConversationViewModel>(Conversations);
    }

    public ProjectWorkspace Project { get; }
    public ObservableCollection<ConversationViewModel> Conversations { get; }
    public ObservableCollection<ConversationViewModel> VisibleConversations { get; }
    public string Name => Project.Name;
    public string Path => Project.Path;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ConversationViewModel CreateConversation()
    {
        // Insert at the top so the newest conversation appears first.
        var conversation = new Conversation
        {
            ProjectId = Project.Id,
            Title = "新对话",
            UpdatedAt = DateTimeOffset.Now
        };
        Project.Conversations.Insert(0, conversation);
        var viewModel = new ConversationViewModel(conversation);
        Conversations.Insert(0, viewModel);
        VisibleConversations.Insert(0, viewModel);
        return viewModel;
    }

    public ConversationViewModel? FindUnstartedConversation()
    {
        return Conversations.FirstOrDefault(conversation => conversation.Messages.Count == 0);
    }

    public void ApplyConversationFilter(string searchText)
    {
        // Rebuild only the visible list; Conversations remains the source of truth.
        var normalized = searchText.Trim();
        VisibleConversations.Clear();
        foreach (var conversation in Conversations)
        {
            if (conversation.ApplySearch(normalized))
            {
                VisibleConversations.Add(conversation);
            }
        }
    }
}
