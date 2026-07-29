using AIChat.App.Avalonia.Composition;

namespace AIChat.Tests.Composition;

// PR-8 tests: AvaloniaProjectPicker returns null when there is no TopLevel
// bound to it (e.g. when the window hasn't been constructed yet). The
// actual file-system dialog is interactive and not exercised here; that
// path is covered by the smoke-test app launch.
public class AvaloniaProjectPickerTests
{
    [Fact]
    public async Task PickProjectFolderAsync_WithNoTopLevel_ReturnsNull()
    {
        var picker = new AvaloniaProjectPicker();

        var path = await picker.PickProjectFolderAsync();

        Assert.Null(path);
    }
}
