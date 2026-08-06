using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace AIChat.App.Avalonia.Composition;

// Default IClipboardService that wraps Avalonia 12's TopLevel.Clipboard.
// The view code-behind injects the service and sets TopLevel once the
// window has been constructed (the service is a singleton, so the
// topLevel is set once at startup, mirroring AvaloniaProjectPicker).
//
// Avalonia 12 replaced the simple SetTextAsync API with an
// IAsyncDataTransfer / DataFormat<T> surface. The convenience extension
// ClipboardExtensions.SetValueAsync(DataFormat<string>, string) is the
// shortest path for "set plain text" and is what we use here. The
// underlying platform implementation handles the rest (Windows OLE,
// macOS NSPasteboard, X11 selection, etc.).
public sealed class AvaloniaClipboardService : IClipboardService
{
    public TopLevel? TopLevel { get; set; }

    public bool IsAvailable => TopLevel?.Clipboard is not null;

    public Task SetTextAsync(string text)
    {
        var clipboard = TopLevel?.Clipboard;
        if (clipboard is null)
        {
            return Task.CompletedTask;
        }

        return clipboard.SetValueAsync(DataFormat.Text, text);
    }

    public async Task<Bitmap?> TryGetBitmapAsync()
    {
        var clipboard = TopLevel?.Clipboard;
        if (clipboard is null)
        {
            return null;
        }

        // ClipboardExtensions.TryGetBitmapAsync hides the IAsyncDataTransfer
        // dance: it asks the clipboard for an image and decodes it
        // (PNG / JPEG / etc.) into a Bitmap. The IAsyncDataTransfer
        // returned by the clipboard MUST be disposed by the caller —
        // TryGetBitmapAsync handles that internally.
        return await clipboard.TryGetBitmapAsync();
    }

    public async Task<string?> TryGetTextAsync()
    {
        var clipboard = TopLevel?.Clipboard;
        if (clipboard is null)
        {
            return null;
        }
        // ClipboardExtensions.TryGetTextAsync handles the
        // IAsyncDataTransfer disposable. Returns null when
        // the clipboard holds a non-text payload (image,
        // file list, custom format, etc.) — the caller
        // falls through to its no-text path.
        return await clipboard.TryGetTextAsync();
    }
}
