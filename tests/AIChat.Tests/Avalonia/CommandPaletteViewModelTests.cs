using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

public sealed class CommandPaletteViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteSelectedAsync_ReturnsActionCloseDecision(bool shouldClose)
    {
        var called = false;
        var viewModel = new CommandPaletteViewModel();
        viewModel.RegisterCommands(
        [
            new CommandItem("test", "", "", "", () =>
            {
                called = true;
                return Task.FromResult(shouldClose);
            })
        ]);

        var result = await viewModel.ExecuteSelectedAsync();

        Assert.True(called);
        Assert.Equal(shouldClose, result);
    }

    [Fact]
    public async Task ExecuteSelectedAsync_PropagatesActionFailureToViewBoundary()
    {
        var viewModel = new CommandPaletteViewModel();
        viewModel.RegisterCommands(
        [
            new CommandItem("test", "", "", "", () =>
                Task.FromException<bool>(new InvalidOperationException("failed")))
        ]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.ExecuteSelectedAsync());

        Assert.Equal("failed", error.Message);
    }
}
