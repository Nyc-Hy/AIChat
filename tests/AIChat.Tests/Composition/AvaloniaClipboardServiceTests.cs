using AIChat.App.Avalonia.Composition;

namespace AIChat.Tests.Composition;

// PR-equivalent: AvaloniaClipboardService mirrors AvaloniaProjectPicker's
// "no TopLevel = no-op" contract. The actual platform clipboard call is
// only reachable when the view code-behind has wired the TopLevel; tests
// run without a window so we only assert the safe no-op behaviour.
public class AvaloniaClipboardServiceTests
{
    [Fact]
    public void IsAvailable_WithoutTopLevel_IsFalse()
    {
        var service = new AvaloniaClipboardService();

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task SetTextAsync_WithoutTopLevel_CompletesWithoutException()
    {
        var service = new AvaloniaClipboardService();

        // Should not throw and should return a completed task. The
        // platform clipboard is not actually invoked; we silently skip
        // the write so tests stay headless and deterministic.
        await service.SetTextAsync("hello");
    }
}
