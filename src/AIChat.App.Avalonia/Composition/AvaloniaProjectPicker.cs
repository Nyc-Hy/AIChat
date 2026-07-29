using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AIChat.App.Avalonia.Composition;

// Default IProjectPicker that wraps Avalonia's StorageProvider. The view
// code-behind injects the picker and sets TopLevel once the window has
// been constructed (the picker is a singleton, so the topLevel is set
// once at startup).
public sealed class AvaloniaProjectPicker : IProjectPicker
{
    public TopLevel? TopLevel { get; set; }

    public async Task<string?> PickProjectFolderAsync(CancellationToken cancellationToken = default)
    {
        if (TopLevel is null)
        {
            return null;
        }

        var folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择项目目录",
            AllowMultiple = false
        });
        cancellationToken.ThrowIfCancellationRequested();

        var folder = folders.FirstOrDefault();
        return folder?.Path.LocalPath is { Length: > 0 } path ? path : null;
    }
}
