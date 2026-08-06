namespace AIChat.App.Avalonia.Composition;

// Boundary between the view layer and the file-system dialog system. The
// view code-behind passes itself in as the TopLevel so the dialog can
// attach to the right window. Returns a PickerResult that distinguishes
// user-picked / user-cancelled / dialog-failed so the caller can decide
// whether to surface anything to the user (cancelled = silent, failed =
// status message).
public interface IProjectPicker
{
    Task<PickerResult> PickProjectFolderAsync(CancellationToken cancellationToken = default);
}
