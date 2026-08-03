using System.Collections.ObjectModel;
using System.IO;
using AIChat.Abstractions.Configuration;
using AIChat.Domain.Artifacts;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Holds the user-attached files (pasted images ⌘V and dropped files
// drag-and-drop) that should travel with the NEXT message. The flow:
//   1. User pastes an image (⌘V) or drops a file into the composer.
//   2. The view code-behind copies the bytes to a managed temp file
//      in PendingAttachmentsViewModel.StorageDirectory and adds a
//      PendingAttachmentViewModel here.
//   3. The user can see the chips above the input and remove any
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
    // Where pasted / dropped files live until they're sent (or
    // removed). Lives in the user-level application data folder so
    // the files don't pollute the project tree. Survives app
    // restarts — useful if the user starts a new conversation right
    // after pasting / dropping.
    public static string StorageDirectory { get; } =
        AppRuntimeProfile.PendingAttachmentsDirectory;

    public ObservableCollection<PendingAttachmentViewModel> Attachments { get; } = [];

    public bool HasAttachments => Attachments.Count > 0;
    public int Count => Attachments.Count;

    static PendingAttachmentsViewModel()
    {
        // Best-effort cleanup of stale files from a previous run
        // (the user pasted / dropped a file but the app crashed /
        // the user force-quit before sending). The directory exists
        // for IN-FLIGHT attachments only — anything left behind on
        // startup is abandoned and safe to delete.
        try
        {
            if (Directory.Exists(StorageDirectory))
            {
                foreach (var stale in Directory.GetFiles(StorageDirectory, "pasted-*.png"))
                {
                    try { File.Delete(stale); } catch { /* best effort */ }
                }
                foreach (var stale in Directory.GetFiles(StorageDirectory, "attached-*.*"))
                {
                    try { File.Delete(stale); } catch { /* best effort */ }
                }
            }
            Directory.CreateDirectory(StorageDirectory);
        }
        catch { /* best effort */ }
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

        var attachment = new PendingAttachmentViewModel(
            fullPath, fileName, fileName, "image/png", thumbnail);
        Attachments.Add(attachment);
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(Count));
        return attachment;
    }

    // Add a file dragged from the OS file manager (or any other
    // file source). Copies the bytes to a managed location so the
    // in-VM lifecycle is consistent with the paste flow — the
    // managed copy is the source of truth from the moment AddFile
    // returns, and Dispose() removes it when the user removes the
    // row or the send finishes.
    //
    // Image MIMEs get a decoded thumbnail Bitmap; everything else
    // gets a null Thumbnail and the XAML falls back to a generic
    // file chip (filename + extension badge).
    //
    // The file extension is preserved (rather than rewritten to
    // .png) so downstream classifiers (InputArtifactClassifier.
    // DetermineKind) and extractors (pdf/docx/xlsx parsers) work
    // from the real on-disk extension. The file name uses an
    // "attached-" prefix to make stale-file cleanup on next
    // startup trivial. The original file name is preserved on the
    // VM as DisplayName so the user sees the name they recognise
    // (not the internal "attached-{guid}." shape).
    public PendingAttachmentViewModel AddFile(string sourcePath, string? mimeType = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Dropped file no longer exists on disk.", sourcePath);
        }

        Directory.CreateDirectory(StorageDirectory);
        var id = Guid.NewGuid().ToString("N");
        var sourceName = Path.GetFileName(sourcePath);
        var extension = Path.GetExtension(sourceName);
        if (string.IsNullOrEmpty(extension))
        {
            // Files with no extension (e.g. a LICENSE or README with
            // no .md/.txt suffix) still need a suffix to make the
            // managed copy name unique. ".bin" is the lowest-common-
            // denominator catch-all that keeps the classifier from
            // having to special-case "no extension".
            extension = ".bin";
        }
        var fileName = $"attached-{id}{extension}";
        var fullPath = Path.Combine(StorageDirectory, fileName);

        // Copy instead of move so a transient drop-then-cancel path
        // (or a send that fails partway) doesn't leave the user's
        // file in a half-moved state.
        File.Copy(sourcePath, fullPath, overwrite: false);

        var resolvedMime = string.IsNullOrWhiteSpace(mimeType)
            ? GuessMimeType(extension)
            : mimeType!;
        var isImage = LooksLikeImage(resolvedMime, extension);
        Bitmap? thumbnail = null;
        if (isImage)
        {
            try
            {
                // Decode a separate Bitmap instance for the XAML
                // thumbnail. The decoded instance lives as long as
                // the PendingAttachmentViewModel (disposed when the
                // user removes the row or sends + clears).
                thumbnail = new Bitmap(fullPath);
            }
            catch
            {
                // Some image files are valid for the classifier
                // (extension matches) but fail Avalonia's strict
                // decoder. Drop the thumbnail and fall back to the
                // generic chip — the artifact itself is still
                // useful, the user just doesn't get a preview.
                thumbnail = null;
            }
        }

        var attachment = new PendingAttachmentViewModel(
            fullPath, fileName, sourceName, resolvedMime, thumbnail);
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

    // Cheap, deterministic extension → mime lookup. Keeps the
    // composer path off the platform's IContentTypeRegistry (which
    // is per-platform and noisy on macOS where e.g. .md is "text/
    // x-markdown" by default, breaking equal-to-expected-claim
    // tests). The classifier later re-runs InputArtifactClassifier.
    // DetermineKind on the same file so anything we send down the
    // pipeline is consistent.
    private static string GuessMimeType(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "svg" => "image/svg+xml",
            "txt" => "text/plain",
            "md" or "markdown" => "text/markdown",
            "json" => "application/json",
            "xml" => "application/xml",
            "yaml" or "yml" => "application/yaml",
            "csv" => "text/csv",
            "tsv" => "text/tab-separated-values",
            "pdf" => "application/pdf",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
    }

    private static bool LooksLikeImage(string mimeType, string extension)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext is "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp" or "svg";
    }
}

// One row in the pending-attachments strip. Holds the path to the
// saved file, its detected MimeType (used by PromotePendingAttachments
// to write the right InputArtifactCreateRequest without hardcoding
// image/png), and a thumbnail Bitmap the XAML can bind to — null
// for non-image attachments, in which case the XAML shows a generic
// file chip. The thumbnail is a separate Bitmap instance from any
// source bitmap (the paste bitmap is disposed immediately after the
// PNG is written, the source file is left at the user's path) so
// the XAML can hold onto this one for as long as the row is
// visible.
public sealed class PendingAttachmentViewModel : ObservableObject, IDisposable
{
    public string FilePath { get; }
    public string FileName { get; }
    public string MimeType { get; }
    public Bitmap? Thumbnail { get; }

    // Drives the XAML template: image rows show the bitmap, non-
    // image rows show a generic file icon. Recomputed from
    // MimeType + extension so any future MimeType change re-flows
    // the chip without callers having to remember to flip a
    // separate flag.
    public bool IsImage { get; }

    // Display label: the original file name the user recognises
    // (e.g. "report.pdf" or "screenshot.png"). Stored explicitly
    // on the VM so XAML can bind to it without re-deriving from
    // the internal "attached-{guid}." / "pasted-{guid}." path.
    // The paste path passes the bare PNG filename; the drop path
    // passes the source name so the user always sees what they
    // actually attached.
    public string DisplayName { get; }

    // 1.0.1: size of the on-disk file (the managed copy in
    // PendingAttachmentsViewModel.StorageDirectory, not the
    // original drop source — the copy is what travels with the
    // message). Captured at construction so the displayed value
    // doesn't drift if the original file changes after the user
    // drops it. Surfaced to the chip so a daily-driver user can
    // see "847 KB" / "12.4 MB" next to a filename before they
    // send — without it, a 50 MB PDF and a 50 KB PDF look the
    // same in the strip and a user who drops ten large files
    // only realises they've blown the context budget when the
    // agent runner comes back with a token-limit error.
    public long ByteCount { get; }

    // Human-readable size for the chip. Formatted once at
    // construction (the byte count is immutable per attachment,
    // so re-formatting on every binding is wasted work) using
    // the same binary units Finder / Explorer use so the
    // numbers line up with what the user just saw in the OS
    // file manager. 0 bytes is rendered as "0 B" rather than
    // empty so the chip stays visually balanced.
    public string SizeDisplay { get; }

    public PendingAttachmentViewModel(
        string filePath,
        string fileName,
        string displayName,
        string mimeType,
        Bitmap? thumbnail)
    {
        FilePath = filePath;
        FileName = fileName;
        DisplayName = displayName;
        MimeType = mimeType;
        Thumbnail = thumbnail;
        IsImage = thumbnail is not null;
        // Read once from disk at construction. FileInfo.Length
        // is cheap (it's a stat, not a read) and the file is
        // already on disk because the paste / drop path writes
        // the managed copy before constructing this VM. The byte
        // count is what travels with the message — the original
        // source may have changed after the drop and we don't
        // want the chip to disagree with what the agent will
        // actually see.
        ByteCount = SafeGetFileLength(filePath);
        SizeDisplay = FormatByteCount(ByteCount);
    }

    private static long SafeGetFileLength(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch
        {
            // File got moved / deleted between copy and
            // construction (rare, but it can happen if the
            // user's AV quarantines the managed copy). The
            // chip still shows the name + a 0 B size — a
            // visual signal that something's off, without
            // crashing the strip.
            return 0;
        }
    }

    // Binary units (KiB / MiB / GiB), labelled with the SI
    // suffix the OS file manager uses ("KB" / "MB" / "GB") so
    // the number reads naturally next to the filename. Jumps
    // at 1.0 of the next unit rather than rounding up — a
    // 1.49 MB file reads as "1.4 MB", not "2 MB", because the
    // user comparing the chip against Finder expects the
    // same level of precision.
    private static string FormatByteCount(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }

    public void Dispose()
    {
        Thumbnail?.Dispose();
        // Best-effort delete of the on-disk file. If it fails
        // (another process has it open, perms, etc.) the file just
        // lingers until the next startup sweep picks it up.
        try { File.Delete(FilePath); } catch { /* best effort */ }
    }
}
