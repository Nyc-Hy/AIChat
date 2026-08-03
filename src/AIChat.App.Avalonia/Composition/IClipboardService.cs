using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace AIChat.App.Avalonia.Composition;

// Boundary between the view layer and the platform clipboard. The view
// code-behind sets TopLevel on the concrete implementation once the
// window is constructed (same pattern as IProjectPicker). IsAvailable
// lets callers fail gracefully when the clipboard is unreachable
// (e.g. during automated tests where no TopLevel is wired).
public interface IClipboardService
{
    bool IsAvailable { get; }

    Task SetTextAsync(string text);

    // Reads plain text from the clipboard. Returns null when
    // the clipboard is empty, holds a non-text payload, or
    // the platform can't surface a string (e.g. some
    // headless test hosts). The first-slice of the Wave 7
    // "剪贴板快照" source uses this; the agent-loop
    // context builder will eventually read it too so the
    // user can @-reference a recent clipboard capture.
    Task<string?> TryGetTextAsync();

    // Reads an image from the clipboard if one is currently on it.
    // Returns null for text-only clipboards or when the platform
    // can't surface a Bitmap. The caller owns the returned Bitmap
    // and must dispose it.
    Task<Bitmap?> TryGetBitmapAsync();
}
