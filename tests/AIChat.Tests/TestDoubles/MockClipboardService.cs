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

    public bool IsAvailable => true;

    public Task SetTextAsync(string text)
    {
        LastSetText = text;
        SetTextCallCount++;
        return Task.CompletedTask;
    }

    public Task<Bitmap?> TryGetBitmapAsync()
    {
        return Task.FromResult<Bitmap?>(null);
    }
}
