using AIChat.App.Avalonia.Composition;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace AIChat.Tests.TestDoubles;

// In-memory IClipboardService for unit tests. Records the
// last set text so assertions can verify what the host tried
// to copy, and reports IsAvailable = true so the call path is
// not short-circuited by the "no TopLevel" guard the real
// AvaloniaClipboardService uses.
public sealed class MockClipboardService : IClipboardService
{
    public string? LastSetText { get; private set; }
    public int SetTextCallCount { get; private set; }

    // The Wave 7 source-capture path reads this on
    // "剪贴板快照" clicks. Tests set it to the text
    // they want the capture flow to see.
    public string? QueuedClipboardText { get; set; }

    public bool IsAvailable => true;

    public Task SetTextAsync(string text)
    {
        LastSetText = text;
        SetTextCallCount++;
        return Task.CompletedTask;
    }

    public Task<string?> TryGetTextAsync()
    {
        return Task.FromResult(QueuedClipboardText);
    }

    public Task<Bitmap?> TryGetBitmapAsync()
    {
        return Task.FromResult<Bitmap?>(null);
    }
}
