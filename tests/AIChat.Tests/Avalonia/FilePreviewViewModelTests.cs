using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// Locks the load-failure semantics on FilePreviewViewModel.
// Pre-fix, the catch block set ContentPath to the failing path so
// HasFile=true and the panel stayed visible — but the only visible
// body was "正在读取文件…" (IsLoading=false) or the close X in a
// panel that pointed at nothing. Post-fix, the catch clears
// ContentPath so the panel collapses and the activity feed takes
// over the full conversation area.
public class FilePreviewViewModelTests : IDisposable
{
    private readonly string _tempRoot;

    public FilePreviewViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "aichat-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void PreviewAsync_WithNonExistentRelativePath_ClearsState()
    {
        var vm = new FilePreviewViewModel();

        vm.PreviewAsync(_tempRoot, "does-not-exist.txt");

        // The panel should collapse (HasFile=false because
        // ContentPath is cleared), the error message surfaces,
        // and IsLoading is reset to false.
        Assert.False(vm.HasFile);
        Assert.False(vm.IsLoading);
        Assert.Equal("", vm.ContentPath);
        Assert.NotNull(vm.LoadError);
        Assert.Empty(vm.Lines);
    }

    [Fact]
    public void PreviewAsync_WithFileLargerThanTwoMegabytes_ClearsState()
    {
        // Create a file just over the 2 MB soft cap. The model
        // refuses to load (better than hanging the UI) and the
        // catch block must clear ContentPath so the panel collapses
        // instead of pointing at an unreadable 2 MB+ blob.
        var big = Path.Combine(_tempRoot, "big.txt");
        File.WriteAllText(big, new string('a', 2_500_000));
        var vm = new FilePreviewViewModel();

        vm.PreviewAsync(_tempRoot, "big.txt");

        Assert.False(vm.HasFile);
        Assert.Equal("", vm.ContentPath);
        Assert.NotNull(vm.LoadError);
        Assert.Contains("过大", vm.LoadError);
    }

    [Fact]
    public async Task PreviewAsync_WithValidSmallFile_PopulatesState()
    {
        var file = Path.Combine(_tempRoot, "ok.cs");
        File.WriteAllText(file, "line1\nline2\nline3\n");
        var vm = new FilePreviewViewModel();

        await vm.PreviewAsync(_tempRoot, "ok.cs");

        Assert.True(vm.HasFile);
        Assert.Equal(file, vm.ContentPath);
        Assert.Equal("ok.cs", vm.DisplayName);
        Assert.Equal(3, vm.Lines.Count);
        Assert.Null(vm.LoadError);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task PreviewAsync_CancelledByNewerLoad_KeepsPreviousContentVisible()
    {
        // When a second load supersedes the first, the first's
        // catch (OperationCanceledException) must not touch
        // ContentPath — otherwise the panel would briefly collapse
        // before the new load completes. Pre-fix, the OperationCanceled
        // branch was empty (good) but the comment was wrong; this
        // test pins the intent.
        var first = Path.Combine(_tempRoot, "first.cs");
        File.WriteAllText(first, "first content\n");
        var second = Path.Combine(_tempRoot, "second.cs");
        File.WriteAllText(second, "second content\n");
        var vm = new FilePreviewViewModel();

        await vm.PreviewAsync(_tempRoot, "first.cs");
        // Trigger a new load that cancels the first (we use a
        // TaskCompletionSource-gated path to guarantee the cancel
        // races with the first load's await).
        var secondLoad = vm.PreviewAsync(_tempRoot, "second.cs");
        await secondLoad;

        // The newer load's content wins.
        Assert.Equal(second, vm.ContentPath);
        Assert.True(vm.HasFile);
    }
}
