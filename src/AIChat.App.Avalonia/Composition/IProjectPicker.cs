namespace AIChat.App.Avalonia.Composition;

// Boundary between the view layer and the file-system dialog system. The
// view code-behind passes itself in as the TopLevel so the dialog can
// attach to the right window. Returns the picked path, or null when the
// user cancels or no project is selected.
public interface IProjectPicker
{
    Task<string?> PickProjectFolderAsync(CancellationToken cancellationToken = default);
}
