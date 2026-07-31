using System.Collections.ObjectModel;
using System.IO;
using AIChat.Domain.Artifacts;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Holds the user-pasted images that should travel with the NEXT
// message. The flow:
//   1. User pastes an image into the prompt TextBox (⌘V / Ctrl+V).
//   2. The view code-behind saves the bitmap to a managed temp file
//      and adds a PendingAttachmentViewModel here.
//   3. The user can see the thumbnails above the input and remove any
//      they didn't mean to attach.
//   4. When SendTaskCommand runs, the host calls ToArtifacts() with
//      the current conversation / project / message context, the
//      VM materialises a list of InputArtifact records, and the
//      host wires them into the chat request.
//
// The attachment is stored on disk before Send so the file is
// guaranteed to exist by the time the agent tries to read it. The
// InputArtifact is created lazily in ToArtifacts — keeping it lazy
// means the same attachment VM can be sent across multiple
// conversations (the project-id / conversation-id fields are
// per-send, not per-paste).
public sealed partial class PendingAttachmentsViewModel : ViewModelBase
{
    // Where pasted images land until they're sent (or removed).
    // Lives in the user-level application data folder so the files
    // don't pollute the project tree. Survives app restarts — useful
    // if the user starts a new conversation right after pasting.
    public static string StorageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AIChat", "pending-attachments");

    public ObservableCollection<PendingAttachmentViewModel> Attachments { get; } = [];

    public bool HasAttachments => Attachments.Count > 0;
    public int Count => Attachments.Count;

    static PendingAttachmentsViewModel()
    {
        // Best-effort pre-create; the directory is also created on
        // first save so this is purely a hint to the OS that we own
        // the path.
        try { Directory.CreateDirectory(StorageDirectory); } catch { /* best effort */ }
    }

    // Add a pasted image. Saves the bitmap bytes to disk and
    // allocates a thumbnail bitmap for the UI. The clipboard Bitmap
    // is disposed after the PNG bytes are written — we only need
    // it long enough to extract the encoded stream.
    public PendingAttachmentViewModel AddPastedImage(Bitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        Directory.CreateDirectory(StorageDirectory);
        var id = Guid.NewGuid().ToString("N");
        var fileName = $"pasted-{id}.png";
        var fullPath = Path.Combine(StorageDirectory, fileName);

        bitmap.Save(fullPath);
        // Decode a separate Bitmap instance for the XAML thumbnail —
        // the original paste bitmap is disposed right after this
        // method returns. The decoded instance lives as long as the
        // PendingAttachmentViewModel (disposed when the user removes
        // the row or sends + clears).
        var thumbnail = new Bitmap(fullPath);

        var attachment = new PendingAttachmentViewModel(fullPath, fileName, thumbnail);
        Attachments.Add(attachment);
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(Count));
        return attachment;
    }

    [RelayCommand]
    private void Remove(PendingAttachmentViewModel? attachment)
    {
        if (attachment is null)
        {
            return;
        }
        attachment.Dispose();
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(Count));
    }

    public void Clear()
    {
        foreach (var attachment in Attachments)
        {
            attachment.Dispose();
        }
        Attachments.Clear();
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(Count));
    }
}

// One row in the pending-attachments strip. Holds the path to the
// saved image and a thumbnail Bitmap the XAML can bind to. The
// thumbnail is a separate Bitmap instance from the original paste
// (the paste bitmap is disposed immediately after the PNG is
// written) so the XAML can hold onto this one for as long as the
// row is visible.
public sealed class PendingAttachmentViewModel : ObservableObject, IDisposable
{
    public string FilePath { get; }
    public string FileName { get; }
    public Bitmap Thumbnail { get; }

    public PendingAttachmentViewModel(string filePath, string fileName, Bitmap thumbnail)
    {
        FilePath = filePath;
        FileName = fileName;
        Thumbnail = thumbnail;
    }

    public void Dispose()
    {
        Thumbnail.Dispose();
        // Best-effort delete of the on-disk file. If it fails
        // (another process has it open, perms, etc.) the file just
        // lingers until the next save replaces it.
        try { File.Delete(FilePath); } catch { /* best effort */ }
    }
}
