using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Moq;

namespace AIChat.Tests.Avalonia;

// Wave 2: ConversationListViewModel 切到 v1 (ChatSession + 外部 sessions)。
// Refresh 现在吃 (project, sessions, preferredId) 而不是只看 project 嵌入的 conversations。
public sealed class ConversationListViewModelTests
{
    [Fact]
    public void Refresh_WithNullProject_ShowsNewPlaceholderAndRaisesEvent()
    {
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.Refresh(project: null, sessions: []);

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
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());

        vm.Refresh(project, sessions: []);

        var card = Assert.Single(vm.Conversations);
        Assert.Equal("new", card.Id);
    }

    [Fact]
    public void Refresh_WithProjectHavingConversations_PicksMostRecentAndRaisesEvent()
    {
        var older = NewSession("older", "Old", DateTimeOffset.Now.AddDays(-2));
        var newer = NewSession("newer", "New", DateTimeOffset.Now);
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        vm.Refresh(project, sessions: [older, newer]);

        Assert.Equal(2, vm.Conversations.Count);
        Assert.Equal("newer", vm.Conversations[0].Id);
        Assert.Equal("newer", vm.SelectedConversationCard!.Id);
        var args = Assert.Single(captured);
        Assert.Same(newer, args.Conversation);
        Assert.Equal("已打开对话：New", args.StatusMessage);
    }

    [Fact]
    public void SelectConversation_WithNewId_ShowsNewPlaceholderAndRaisesEvent()
    {
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project: null, sessions: []);
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
        var target = NewSession("target", "Target", DateTimeOffset.Now);
        var other = NewSession("other", "Other", DateTimeOffset.Now.AddDays(-1));
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, sessions: [other, target]);
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
        var session = NewSession("c1", "One", DateTimeOffset.Now);
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, sessions: [session]);
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
        var a = NewSession("a", "A", DateTimeOffset.Now.AddMinutes(-2));
        var b = NewSession("b", "B", DateTimeOffset.Now);
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, sessions: [a, b]);

        vm.SelectConversationCommand.Execute("a");

        var cardA = vm.Conversations.First(c => c.Id == "a");
        var cardB = vm.Conversations.First(c => c.Id == "b");
        Assert.True(cardA.IsSelected);
        Assert.False(cardB.IsSelected);
    }

    [Fact]
    public async Task RemoveConversation_RemovesFromListAndSaves()
    {
        var a = NewSession("a", "First", DateTimeOffset.UtcNow);
        var b = NewSession("b", "Second", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: [a, b]);
        Assert.Equal(2, vm.Conversations.Count);

        var captured = new List<ConversationSelectedEventArgs>();
        vm.ConversationSelected += (_, args) => captured.Add(args);

        await vm.RemoveConversationCommand.ExecuteAsync("a");

        // v1: vm 写 SaveSessionsAsync(sessions),不是 SaveProjectsAsync
        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.Is<IReadOnlyList<ChatSession>>(list => list.Count == 1 && list[0].Id == "b"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(vm.Conversations);
        Assert.Equal("b", vm.Conversations[0].Id);

        var deleteEvent = captured.FirstOrDefault(args => args.StatusMessage?.Contains("已删除对话") == true);
        Assert.NotNull(deleteEvent);
        Assert.Null(deleteEvent!.Conversation);
        Assert.Contains("First", deleteEvent.StatusMessage);
    }

    [Fact]
    public async Task RemoveConversation_WithUnknownId_DoesNothing()
    {
        var project = NewProject("p1", "/tmp/alpha");
        var session = NewSession("a", "First", DateTimeOffset.UtcNow);
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: [session]);

        await vm.RemoveConversationCommand.ExecuteAsync("nope");

        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveConversation_WithNewPlaceholder_DoesNothing()
    {
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: []);

        await vm.RemoveConversationCommand.ExecuteAsync("new");

        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameConversation_UpdatesTitleAndSaves()
    {
        var session = NewSession("a", "Old title", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: [session]);

        await vm.RenameConversationAsync("a", "  New title  ");

        Assert.Equal("New title", session.Title);
        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.Is<IReadOnlyList<ChatSession>>(list => list.Any(s => s.Id == "a" && s.Title == "New title")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameConversation_WithNewPlaceholder_DoesNothing()
    {
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: []);

        await vm.RenameConversationAsync("new", "Whatever");

        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameConversation_WithWhitespaceOnly_DoesNothing()
    {
        var session = NewSession("a", "Original", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: [session]);

        await vm.RenameConversationAsync("a", "   ");

        Assert.Equal("Original", session.Title);
        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameConversation_WithUnchangedTitle_DoesNothing()
    {
        var session = NewSession("a", "Same", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: [session]);

        await vm.RenameConversationAsync("a", "  Same  ");

        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportConversationToPath_WritesMarkdown()
    {
        // 2026-08-03: the right-click 'Export as Markdown' menu
        // delegates the file pick to MainWindow's code-behind
        // and the actual file write to this method. Verify the
        // contract: given a real conversation id + a writable
        // path, the file is created with the rendered Markdown.
        var session = NewSession("c1", "Title", DateTimeOffset.Now);
        session.Messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = "怎么配 keychain?",
            CreatedAt = DateTimeOffset.Now,
        });
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, [session]);

        var path = Path.Combine(Path.GetTempPath(), "aichat-export-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            var bytes = await vm.ExportConversationToPathAsync("c1", path);
            Assert.NotNull(bytes);
            Assert.True(bytes > 0);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("# Title", content);
            Assert.Contains("怎么配 keychain?", content);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task ExportConversationToPath_ReturnsNullForPlaceholderId()
    {
        var session = NewSession("c1", "Title", DateTimeOffset.Now);
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, [session]);

        // "new" is the placeholder id; we must not let the user
        // export a phantom empty session.
        var bytes = await vm.ExportConversationToPathAsync("new", "/tmp/whatever.md");
        Assert.Null(bytes);
    }

    [Fact]
    public async Task ExportConversationToPath_ReturnsNullForUnknownId()
    {
        var session = NewSession("c1", "Title", DateTimeOffset.Now);
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, [session]);

        var bytes = await vm.ExportConversationToPathAsync("does-not-exist", "/tmp/whatever.md");
        Assert.Null(bytes);
    }

    [Fact]
    public async Task ExportConversationToPath_ReturnsNullOnUnwritablePath()
    {
        // Force a write failure: a directory that does not exist
        // throws DirectoryNotFoundException, which the method
        // converts to a null return so the host can toast '导出失败'.
        var session = NewSession("c1", "Title", DateTimeOffset.Now);
        var project = NewProject("p1", "/tmp");
        var vm = new ConversationListViewModel(Mock.Of<IAppRepository>());
        vm.Refresh(project, [session]);

        var badPath = Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "x.md");
        var bytes = await vm.ExportConversationToPathAsync("c1", badPath);
        Assert.Null(bytes);
    }

    private static WorkspaceProject NewProject(string id, string path)
    {
        var folderId = "f1";
        return new WorkspaceProject
        {
            Id = id,
            Name = id,
            Folders = [new WorkspaceFolder { Id = folderId, Path = path }],
            PrimaryFolderId = folderId,
        };
    }

    private static ChatSession NewSession(string id, string title, DateTimeOffset updatedAt)
        => new Project
        {
            Id = id,
            WorkspaceId = "p1",
            Title = title,
            UpdatedAt = updatedAt,
        };

    // ---- 1.0.6: RemoveConversation undo affordance ----

    [Fact]
    public async Task RemoveConversation_ShowsUndoToast()
    {
        // A user who deletes a conversation
        // gets a 3-second "已删除 X [撤销]"
        // toast alongside the physical
        // delete. The toast item is the
        // service's normal surface; the
        // XAML renders the "撤销" button
        // because ToastItem.HasAction is
        // true. The save is the same one
        // the existing test verifies —
        // this one is about the toast
        // being present, not the
        // deletion itself.
        var a = NewSession("a", "First", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var toast = new ToastService(action => action());
        var vm = new ConversationListViewModel(repository, toast);
        vm.Refresh(project, sessions: [a]);

        await vm.RemoveConversationCommand.ExecuteAsync("a");

        var undoToast = Assert.Single(toast.Toasts);
        Assert.True(undoToast.HasAction);
        Assert.Equal("撤销", undoToast.ActionLabel);
        Assert.Equal(ToastLevel.Warning, undoToast.Level);
        Assert.Contains("First", undoToast.Message);
    }

    [Fact]
    public async Task RemoveConversation_UndoAction_RestoresSessionToList()
    {
        // Clicking "撤销" on the toast
        // re-inserts the deleted session
        // and re-saves. The host's
        // ActivityFeed still shows the
        // "new conversation" prompt (the
        // restore does not auto-select
        // the recovered session — that
        // is a separate decision left
        // for a follow-up) so the
        // contract under test is just
        // "the row comes back in the
        // sidebar". Refresh sorts by
        // UpdatedAt descending, so the
        // recovered row lands at the top
        // because the RestoreConversation
        // path sets the session's
        // UpdatedAt to "now".
        var a = NewSession("a", "First", DateTimeOffset.UtcNow.AddDays(-1));
        var b = NewSession("b", "Second", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var toast = new ToastService(action => action());
        var vm = new ConversationListViewModel(repository, toast);
        vm.Refresh(project, sessions: [a, b]);

        await vm.RemoveConversationCommand.ExecuteAsync("a");
        Assert.Single(vm.Conversations);
        Assert.Equal("b", vm.Conversations[0].Id);

        // The user clicks "撤销" on the toast.
        toast.Toasts[0].InvokeAction();

        Assert.Equal(2, vm.Conversations.Count);
        Assert.Contains(vm.Conversations, card => card.Id == "a");
        Assert.Contains(vm.Conversations, card => card.Id == "b");
        // The re-save lands; the existing
        // RemoveConversation test already
        // pins the save-on-delete contract,
        // so here we just confirm the
        // restore path also persisted.
        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RemoveConversation_WithoutToastService_StillDeletes()
    {
        // The IToastService ctor parameter
        // is optional so the 6 existing
        // test sites that construct the
        // VM directly don't have to wire
        // a mock. The delete must still
        // work when no toast service is
        // present (e.g. headless test
        // host, or a future code path
        // that wants to suppress the
        // undo affordance).
        var a = NewSession("a", "First", DateTimeOffset.UtcNow);
        var b = NewSession("b", "Second", DateTimeOffset.UtcNow);
        var project = NewProject("p1", "/tmp/alpha");
        var repository = Mock.Of<IAppRepository>();
        var vm = new ConversationListViewModel(repository);
        vm.Refresh(project, sessions: [a, b]);

        await vm.RemoveConversationCommand.ExecuteAsync("a");

        // "a" is gone, "b" remains. The
        // vm.Conversations.Count assertion
        // is the smoke test; the save
        // verification pins the side
        // effect that the toast path
        // shares.
        Assert.Single(vm.Conversations);
        Assert.Equal("b", vm.Conversations[0].Id);
        Mock.Get(repository).Verify(repo => repo.SaveSessionsAsync(
            It.IsAny<IReadOnlyList<ChatSession>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
