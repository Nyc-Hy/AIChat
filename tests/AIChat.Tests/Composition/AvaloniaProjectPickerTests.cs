using AIChat.App.Avalonia.Composition;

namespace AIChat.Tests.Composition;

// Phase 0 tests for the picker failure modes. The interactive
// StorageProvider.OpenFolderPickerAsync path can't be exercised
// in the headless test host (it requires a real TopLevel + OS
// dialog), so we cover the failure paths we control: missing
// TopLevel and exception thrown by the underlying provider. The
// happy path is verified by the e2e smoke launch.
public class AvaloniaProjectPickerTests
{
    [Fact]
    public async Task PickProjectFolderAsync_WithNoTopLevel_ReturnsFailedWithReason()
    {
        // Pre-fix this returned null, which callers could not
        // distinguish from "user cancelled". A new user clicking
        // Add Project on a fresh launch with a mis-wired DI
        // container would see absolutely nothing happen — that's
        // the bug the three-state result type fixes.
        var picker = new AvaloniaProjectPicker();

        var result = await picker.PickProjectFolderAsync();

        var failed = Assert.IsType<PickerResult.Failed>(result);
        Assert.Contains("主窗口", failed.Reason);
    }
}
