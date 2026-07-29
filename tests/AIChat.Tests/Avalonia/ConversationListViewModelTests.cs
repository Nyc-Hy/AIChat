using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Avalonia;

// Unit tests for the PR-4 extraction. ConversationListViewModel only
// touches pure CLR types (no Avalonia runtime calls), so these tests run
// without the headless platform.
public class ConversationListViewModelTests
{
    [Fact]
    public void Refresh_WithNullProject_ShowsNewPlaceholderAndRaisesEvent()
    {
        var vm = new ConversationListViewModel();
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.Refresh(project: null);

        var card = Assert.Single(vm.Conversations);
        Assert.Equal("new", card.Id);
        Assert.Same(card, vm.SelectedConversationCard);
        var args = Assert.Single(captured);
        Assert.Null(args.Conversation);
        Assert.Equal("已打开新对话。", args.StatusMessage);
    }

    [Fact]
    public void Refresh_WithProjectHavingNoConversations_ShowsNewPlaceholder()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Empty", Path = "" };
        var vm = new ConversationListViewModel();

        vm.Refresh(project);

        var card = Assert.Single(vm.Conversations);
        Assert.Equal("new", card.Id);
    }

    [Fact]
    public void Refresh_WithProjectHavingConversations_PicksMostRecentAndRaisesEvent()
    {
        var older = NewConversation("older", "Old", DateTimeOffset.Now.AddDays(-2));
        var newer = NewConversation("newer", "New", DateTimeOffset.Now);
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Sample",
            Path = "",
            Conversations = { older, newer }
        };
        var vm = new ConversationListViewModel();
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.Refresh(project);

        Assert.Equal(2, vm.Conversations.Count);
        Assert.Equal("newer", vm.Conversations[0].Id); // sorted desc by UpdatedAt
        Assert.Equal("newer", vm.SelectedConversationCard!.Id);
        var args = Assert.Single(captured);
        Assert.Same(newer, args.Conversation);
        Assert.Equal("已打开对话：New", args.StatusMessage);
    }

    [Fact]
    public void SelectConversation_WithNewId_ShowsNewPlaceholderAndRaisesEvent()
    {
        // The "new" card is only present when the project is null or has
        // no conversations — that is the only situation where the user can
        // actually pick "new" from the visible list.
        var vm = new ConversationListViewModel();
        vm.Refresh(project: null);
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.SelectConversationCommand.Execute("new");

        Assert.Equal("new", vm.SelectedConversationCard!.Id);
        var args = Assert.Single(captured);
        Assert.Null(args.Conversation);
        Assert.Equal("已打开新对话。", args.StatusMessage);
    }

    [Fact]
    public void SelectConversation_WithKnownId_LoadsThatConversation()
    {
        var target = NewConversation("target", "Target", DateTimeOffset.Now);
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Sample",
            Path = "",
            Conversations = { NewConversation("other", "Other", DateTimeOffset.Now.AddDays(-1)), target }
        };
        var vm = new ConversationListViewModel();
        vm.Refresh(project);
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.SelectConversationCommand.Execute("target");

        var args = Assert.Single(captured);
        Assert.Same(target, args.Conversation);
        Assert.Equal("已打开对话：Target", args.StatusMessage);
    }

    [Fact]
    public void SelectConversation_WithUnknownId_KeepsSelectionAndRaisesNewPromptEvent()
    {
        // When the project has real conversations, an unknown id does not
        // change the list selection — only the activity feed switches to
        // the "new conversation" prompt via the event.
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Sample",
            Path = "",
            Conversations = { NewConversation("c1", "One", DateTimeOffset.Now) }
        };
        var vm = new ConversationListViewModel();
        vm.Refresh(project);
        var originalSelection = vm.SelectedConversationCard;
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.SelectConversationCommand.Execute("nope");

        Assert.Same(originalSelection, vm.SelectedConversationCard);
        var args = Assert.Single(captured);
        Assert.Null(args.Conversation);
    }

    [Fact]
    public void SelectConversation_AppliesSelectionColorToChosenCard()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Sample",
            Path = "",
            Conversations =
            {
                NewConversation("a", "A", DateTimeOffset.Now.AddMinutes(-2)),
                NewConversation("b", "B", DateTimeOffset.Now)
            }
        };
        var vm = new ConversationListViewModel();
        vm.Refresh(project);

        vm.SelectConversationCommand.Execute("a");

        var cardA = vm.Conversations.First(c => c.Id == "a");
        var cardB = vm.Conversations.First(c => c.Id == "b");
        Assert.Equal("#FFFFFF", cardA.Background);
        Assert.Equal("#FFFFFF00", cardB.Background);
    }

    private static Conversation NewConversation(string id, string title, DateTimeOffset updatedAt)
        => new()
        {
            Id = id,
            ProjectId = "p1",
            Title = title,
            UpdatedAt = updatedAt
        };
}
