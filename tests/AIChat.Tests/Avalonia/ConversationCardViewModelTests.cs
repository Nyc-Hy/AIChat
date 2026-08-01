using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// Unit tests for the inline-rename state machine on
// ConversationCardViewModel. The card is a pure CLR type, so these
// run without the headless Avalonia platform — the test baseline
// stays at 634 + 7 new tests.
public class ConversationCardViewModelTests
{
    [Fact]
    public void StartRename_CopiesTitleIntoEditingTitleAndFlipsIsRenaming()
    {
        var card = new ConversationCardViewModel("c1", "Original", "detail", null);

        card.StartRenameCommand.Execute(null);

        Assert.True(card.IsRenaming);
        Assert.Equal("Original", card.EditingTitle);
    }

    [Fact]
    public async Task CommitRenameAsync_WithNewValue_UpdatesTitleAndInvokesCallback()
    {
        var invoked = new List<(string id, string title)>();
        var card = new ConversationCardViewModel(
            "c1",
            "Old",
            "detail",
            (id, title) =>
            {
                invoked.Add((id, title));
                return Task.CompletedTask;
            });

        card.StartRenameCommand.Execute(null);
        card.EditingTitle = "  New  ";
        await card.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal("New", card.Title);
        Assert.False(card.IsRenaming);
        var captured = Assert.Single(invoked);
        Assert.Equal(("c1", "New"), captured);
    }

    [Fact]
    public async Task CommitRenameAsync_WithUnchangedValue_DoesNotInvokeCallback()
    {
        var invoked = 0;
        var card = new ConversationCardViewModel(
            "c1",
            "Same",
            "detail",
            (_, _) => { invoked++; return Task.CompletedTask; });

        card.StartRenameCommand.Execute(null);
        await card.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal(0, invoked);
        Assert.False(card.IsRenaming);
    }

    [Fact]
    public async Task CommitRenameAsync_WithEmptyValue_RollsBackAndDoesNotInvokeCallback()
    {
        var invoked = 0;
        var card = new ConversationCardViewModel(
            "c1",
            "Original",
            "detail",
            (_, _) => { invoked++; return Task.CompletedTask; });

        card.StartRenameCommand.Execute(null);
        card.EditingTitle = "   ";
        await card.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal("Original", card.Title);
        Assert.False(card.IsRenaming);
        Assert.Equal("Original", card.EditingTitle);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task CommitRenameAsync_WithoutCallback_DoesNotThrow()
    {
        // The "new" placeholder card is constructed with a null
        // onTitleChange callback. Commit should still flip state
        // without throwing.
        var card = new ConversationCardViewModel("new", "新任务", "detail", null);
        card.StartRenameCommand.Execute(null);
        card.EditingTitle = "Anything";

        await card.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal("Anything", card.Title);
        Assert.False(card.IsRenaming);
    }

    [Fact]
    public void CancelRename_RollsBackAndExitsEditMode()
    {
        var card = new ConversationCardViewModel("c1", "Original", "detail", null);
        card.StartRenameCommand.Execute(null);
        card.EditingTitle = "Typo in progress";

        card.CancelRenameCommand.Execute(null);

        Assert.False(card.IsRenaming);
        Assert.Equal("Original", card.EditingTitle);
    }

    [Fact]
    public void Title_RaisesPropertyChanged()
    {
        // Lock the IsRenaming re-raise pattern: any future change
        // to the OnTitleChanged partial method must keep firing
        // PropertyChanged so XAML re-binds the display.
        var card = new ConversationCardViewModel("c1", "Original", "detail", null);
        var raised = new List<string?>();
        card.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        card.Title = "Updated";

        Assert.Contains("Title", raised);
    }
}
