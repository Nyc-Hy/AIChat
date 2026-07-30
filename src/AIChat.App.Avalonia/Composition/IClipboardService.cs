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
}
