using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Moq;

namespace AIChat.Tests.Avalonia;

// Unit tests for the PR-4 extraction. ConversationListViewModel only
// touches pure CLR types (no Avalonia runtime calls), so these tests run
// without the headless platform.
public class ConversationListViewModelTests
{
    [Fact]
    public void Refresh_WithNullProject_ShowsNewPlaceholderAndRaisesEvent()
    {
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());
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
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());

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
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());
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
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());
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
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());
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
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());
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
    public void SelectConversation_MarksIsSelectedOnChosenCard()
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
        var vm = new ConversationListViewModel(Mock.Of<AIChat.Abstractions.Persistence.IAppRepository>());
        vm.Refresh(project);

        vm.SelectConversationCommand.Execute("a");

        var cardA = vm.Conversations.First(c => c.Id == "a");
        var cardB = vm.Conversations.First(c => c.Id == "b");
        Assert.True(cardA.IsSelected);
        Assert.False(cardB.IsSelected);
    }

    [Fact]
    public async Task RemoveConversation_RemovesFromProjectAndSaves()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Conversations =
            {
                NewConversation("a", "First", DateTimeOffset.UtcNow),
                NewConversation("b", "Second", DateTimeOffset.UtcNow)
            }
        };
        var repository = Mock.Of<IAppRepository>();
        Mock.Get(repository)
            .Setup(repo => repo.LoadProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { project });
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);
        Assert.Equal(2, vm.Conversations.Count);

        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        await vm.RemoveConversationCommand.ExecuteAsync("a");

        Assert.Single(project.Conversations);
        Assert.Equal("b", project.Conversations[0].Id);
        Assert.Single(vm.Conversations);
        Assert.Equal("b", vm.Conversations[0].Id);
        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.Is<List<ProjectWorkspace>>(list => list.Count == 1 && list[0].Conversations.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        // Refresh fires its own ConversationSelected event when it
        // rebuilds the list after the delete. Filter to the delete
        // event specifically.
        var deleteEvent = captured.FirstOrDefault(args => args.StatusMessage?.Contains("已删除对话") == true);
        Assert.NotNull(deleteEvent);
        Assert.Null(deleteEvent!.Conversation);
        Assert.Contains("First", deleteEvent.StatusMessage);
    }

    [Fact]
    public async Task RemoveConversation_WithUnknownId_DoesNothing()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Conversations = { NewConversation("a", "First", DateTimeOffset.UtcNow) }
        };
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);

        await vm.RemoveConversationCommand.ExecuteAsync("nope");

        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.IsAny<List<ProjectWorkspace>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveConversation_WithNewPlaceholder_DoesNothing()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);

        await vm.RemoveConversationCommand.ExecuteAsync("new");

        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.IsAny<List<ProjectWorkspace>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameConversation_UpdatesTitleAndSaves()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Conversations = { NewConversation("a", "Old title", DateTimeOffset.UtcNow) }
        };
        var repository = Mock.Of<IAppRepository>();
        Mock.Get(repository)
            .Setup(repo => repo.LoadProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { project });
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);

        await vm.RenameConversationAsync("a", "  New title  ");

        Assert.Equal("New title", project.Conversations[0].Title);
        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.IsAny<List<ProjectWorkspace>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameConversation_WithNewPlaceholder_DoesNothing()
    {
        var project = new ProjectWorkspace { Id = "p1", Name = "Alpha", Path = "/tmp/alpha" };
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);

        await vm.RenameConversationAsync("new", "Whatever");

        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.IsAny<List<ProjectWorkspace>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameConversation_WithWhitespaceOnly_DoesNothing()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Conversations = { NewConversation("a", "Original", DateTimeOffset.UtcNow) }
        };
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);

        await vm.RenameConversationAsync("a", "   ");

        Assert.Equal("Original", project.Conversations[0].Title);
        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.IsAny<List<ProjectWorkspace>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameConversation_WithUnchangedTitle_DoesNothing()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Alpha",
            Path = "/tmp/alpha",
            Conversations = { NewConversation("a", "Same", DateTimeOffset.UtcNow) }
        };
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project);

        await vm.RenameConversationAsync("a", "  Same  ");

        Mock.Get(repository).Verify(repo => repo.SaveProjectsAsync(
            It.IsAny<List<ProjectWorkspace>>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
