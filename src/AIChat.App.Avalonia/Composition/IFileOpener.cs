namespace AIChat.App.Avalonia.Composition;

// Boundary for "open a file with the system default app". The
// file tree's double-click affordance goes through this so the
// VM can be tested without actually spawning `open` processes
// during unit tests.
//
// The interface is intentionally narrow: it only takes an
// absolute path and either returns success or throws. There's
// no "open with specific app" overload yet because the daily-
// driver use case is "the user wants to edit this file in
// their IDE" — they pick the IDE, the OS picks the rest.
public interface IFileOpener
{
    void OpenWithSystemApp(string absolutePath);
}
