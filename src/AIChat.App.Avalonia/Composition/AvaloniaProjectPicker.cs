using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AIChat.App.Avalonia.Composition;

// Default IProjectPicker that wraps Avalonia's StorageProvider. The view
// code-behind injects the picker and sets TopLevel once the window has
// been constructed (the picker is a singleton, so the topLevel is set
// once at startup).
//
// Failure-mode semantics:
//   - TopLevel null at call time → Failed("无法绑定到主窗口…") so the
//     caller can show a status message instead of silently no-op'ing
//     (this was the pre-fix "I clicked Add Project and nothing
//     happened" footgun on macOS where the picker ctor raced with
//     the window).
//   - OpenFolderPickerAsync throws (e.g. macOS sandbox / Apple Events
//     denied for unsigned builds) → Failed with the exception message
//     so the user at least sees why nothing happened.
//   - User cancels the dialog (empty array) → Cancelled. The caller
//     suppresses the status message in this case so the user doesn't
//     get nagged for cancelling.
public sealed class AvaloniaProjectPicker : IProjectPicker
{
    public TopLevel? TopLevel { get; set; }

    public async Task<PickerResult> PickProjectFolderAsync(CancellationToken cancellationToken = default)
    {
        if (TopLevel is null)
        {
            return new PickerResult.Failed("无法绑定到主窗口，请重试或重启 AIChat。");
        }

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择项目目录",
                AllowMultiple = false
            });
        }
        catch (Exception ex)
        {
            return new PickerResult.Failed($"打开文件选择对话框失败：{ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return new PickerResult.Cancelled();
        }

        // IStorageItem.Path may be an empty/non-file URI on macOS for
        // folders returned by the native picker. Avalonia's helper is
        // deliberately defensive and returns null instead of letting
        // Uri.LocalPath throw, which would otherwise break first-run.
        var path = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return new PickerResult.Failed("选中的目录路径为空。");
        }

        return new PickerResult.Picked(path);
    }
}
