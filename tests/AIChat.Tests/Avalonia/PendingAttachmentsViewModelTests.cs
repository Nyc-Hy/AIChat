using System.IO;
using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// Unit tests for the pending-attachments strip. The full paste
// pipeline (clipboard read → bitmap save → thumbnail decode →
// add to collection) needs a real GUI to exercise end-to-end, so
// these tests cover the contract surface that doesn't require a
// real Bitmap: collection state, null-safety, file lifecycle, and
// the new drag-and-drop AddFile path (which the old tests
// couldn't exercise because the old API was image-only).
public class PendingAttachmentsViewModelTests : IDisposable
{
    private readonly string _scratchDir;

    public PendingAttachmentsViewModelTests()
    {
        // Per-test scratch directory so the AddFile tests don't
        // collide with each other on parallel test runs. We point
        // the source file at this dir; the VM's StorageDirectory
        // is the real managed copy location and is cleaned up in
        // Dispose.
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "aichat-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        // Best-effort cleanup of any temp files the tests created
        // — both the per-test scratch sources AND the VM's
        // managed copy location.
        try
        {
            if (Directory.Exists(_scratchDir))
            {
                Directory.Delete(_scratchDir, recursive: true);
            }
        }
        catch { /* best effort */ }

        try
        {
            if (Directory.Exists(PendingAttachmentsViewModel.StorageDirectory))
            {
                foreach (var file in Directory.GetFiles(PendingAttachmentsViewModel.StorageDirectory, "pasted-*.png"))
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                }
                foreach (var file in Directory.GetFiles(PendingAttachmentsViewModel.StorageDirectory, "attached-*.*"))
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                }
            }
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void NewCollection_IsEmpty()
    {
        var vm = new PendingAttachmentsViewModel();

        Assert.Empty(vm.Attachments);
        Assert.False(vm.HasAttachments);
        Assert.Equal(0, vm.Count);
    }

    [Fact]
    public void Remove_WithNull_DoesNothing()
    {
        var vm = new PendingAttachmentsViewModel();

        vm.RemoveCommand.Execute(null);

        Assert.Empty(vm.Attachments);
    }

    [Fact]
    public void Clear_OnEmpty_DoesNotThrow()
    {
        var vm = new PendingAttachmentsViewModel();

        vm.Clear();

        Assert.Empty(vm.Attachments);
    }

    [Fact]
    public void AddPastedImage_WithNullBitmap_Throws()
    {
        var vm = new PendingAttachmentsViewModel();

        Assert.Throws<ArgumentNullException>(() => vm.AddPastedImage(null!));
    }

    // ---- AddFile (drag-and-drop) ----

    [Fact]
    public void AddFile_CopiesTextFile_ToManagedStorage()
    {
        var source = Path.Combine(_scratchDir, "notes.md");
        File.WriteAllText(source, "# Hello\n\nworld");
        var vm = new PendingAttachmentsViewModel();

        var attachment = vm.AddFile(source);

        Assert.Single(vm.Attachments);
        Assert.True(attachment.IsImage is false,
            "Markdown isn't an image; IsImage must be false so the XAML shows the file chip.");
        Assert.Null(attachment.Thumbnail);
        Assert.Equal("text/markdown", attachment.MimeType);
        Assert.Equal("notes.md", attachment.DisplayName);
        Assert.True(File.Exists(attachment.FilePath),
            "The managed copy must exist on disk for the artifact pipeline to read it later.");
        Assert.False(string.Equals(source, attachment.FilePath, StringComparison.Ordinal),
            "The managed copy lives in StorageDirectory, not at the user's drop source.");
        Assert.Equal("# Hello\n\nworld", File.ReadAllText(attachment.FilePath));
    }

    [Fact]
    public void AddFile_PreservesExtension_ForClassifier()
    {
        // The downstream InputArtifactClassifier routes on
        // extension (pdf / docx / xlsx / raw text). A dropped
        // .pdf must keep its extension through the lifecycle so
        // the pdf extractor actually runs.
        var source = Path.Combine(_scratchDir, "report.pdf");
        // Just enough bytes that the file is non-empty — the
        // extractor will run on a real PDF later; we only need
        // the extension preserved here.
        File.WriteAllBytes(source, [0x25, 0x50, 0x44, 0x46]);
        var vm = new PendingAttachmentsViewModel();

        var attachment = vm.AddFile(source);

        Assert.Equal(".pdf", Path.GetExtension(attachment.FilePath));
        Assert.Equal("application/pdf", attachment.MimeType);
        Assert.Equal("report.pdf", attachment.DisplayName);
    }

    [Fact]
    public void AddFile_MissingFile_Throws()
    {
        var vm = new PendingAttachmentsViewModel();
        var ghost = Path.Combine(_scratchDir, "no-such-file.png");

        Assert.Throws<FileNotFoundException>(() => vm.AddFile(ghost));
        Assert.Empty(vm.Attachments);
    }

    [Fact]
    public void AddFile_EmptyPath_Throws()
    {
        var vm = new PendingAttachmentsViewModel();

        Assert.Throws<ArgumentException>(() => vm.AddFile(""));
    }

    [Fact]
    public void AddFile_LeavesSourceFile_Intact()
    {
        // The drag-and-drop contract is: the user's file at the
        // source path stays where it is. The VM makes a managed
        // copy; it does NOT move or delete the original.
        var source = Path.Combine(_scratchDir, "keep-me.txt");
        File.WriteAllText(source, "important data");
        var vm = new PendingAttachmentsViewModel();

        vm.AddFile(source);

        Assert.True(File.Exists(source),
            "The source file must remain on disk after the drop — the VM makes a copy, not a move.");
        Assert.Equal("important data", File.ReadAllText(source));
    }

    [Fact]
    public void AddFile_DetectsPngMimeType_ForImage()
    {
        // 1x1 PNG — the minimum valid PNG. We don't need real
        // pixel data to verify that AddFile detected the mime
        // type and accepted the file as an image. The actual
        // bitmap decode is environment-specific (the headless
        // test host uses a stub SkiaSharp that may not decode
        // pixel data), but the mime lookup is deterministic
        // and the IsImage flag is set from the mime.
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=");
        var source = Path.Combine(_scratchDir, "pixel.png");
        File.WriteAllBytes(source, pngBytes);
        var vm = new PendingAttachmentsViewModel();

        var attachment = vm.AddFile(source);

        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal("pixel.png", attachment.DisplayName);
    }

    [Fact]
    public void AddFile_NoExtension_GuessesOctetStream_AndKeepsDisplayName()
    {
        // LICENSE / README / Makefile — files with no extension
        // still need to be addable, just with a generic
        // application/octet-stream mime and a usable display
        // name.
        var source = Path.Combine(_scratchDir, "LICENSE");
        File.WriteAllText(source, "MIT license text");
        var vm = new PendingAttachmentsViewModel();

        var attachment = vm.AddFile(source);

        Assert.False(attachment.IsImage);
        Assert.Equal("application/octet-stream", attachment.MimeType);
        Assert.Equal("LICENSE", attachment.DisplayName);
    }

    [Fact]
    public void AddFile_MultipleFiles_AllPersist()
    {
        var paths = new[]
        {
            Path.Combine(_scratchDir, "a.txt"),
            Path.Combine(_scratchDir, "b.json"),
            Path.Combine(_scratchDir, "c.png"),
        };
        File.WriteAllText(paths[0], "a");
        File.WriteAllText(paths[1], "{}");
        File.WriteAllBytes(paths[2], Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgYAAAAAMAASsJTYQAAAAASUVORK5CYII="));
        var vm = new PendingAttachmentsViewModel();

        foreach (var path in paths)
        {
            vm.AddFile(path);
        }

        Assert.Equal(3, vm.Count);
        Assert.True(vm.HasAttachments);
        Assert.Equal(new[] { "a.txt", "b.json", "c.png" },
            vm.Attachments.Select(a => a.DisplayName).ToArray());
    }

    [Fact]
    public void Remove_DeletesManagedCopy_OnDisk()
    {
        var source = Path.Combine(_scratchDir, "disposable.txt");
        File.WriteAllText(source, "data");
        var vm = new PendingAttachmentsViewModel();
        var attachment = vm.AddFile(source);
        var managedPath = attachment.FilePath;
        Assert.True(File.Exists(managedPath));

        vm.RemoveCommand.Execute(attachment);

        Assert.Empty(vm.Attachments);
        Assert.False(File.Exists(managedPath),
            "Removing a pending attachment should delete the managed copy on disk.");
        Assert.True(File.Exists(source),
            "But the user's source file at the drop origin stays put.");
    }

    [Fact]
    public void AddFile_PreservesOriginalFileName_AsDisplayName()
    {
        // The internal FileName (used for the managed copy on
        // disk) carries the "attached-{guid}." prefix so the
        // stale-file cleanup on next startup can find it. The
        // DisplayName is the original name the user dropped —
        // the XAML binds to DisplayName for the chip label +
        // tooltip, not the internal name.
        var source = Path.Combine(_scratchDir, "screenshot.png");
        // minimal valid png
        File.WriteAllBytes(source, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgYAAAAAMAASsJTYQAAAAASUVORK5CYII="));
        var vm = new PendingAttachmentsViewModel();

        var attachment = vm.AddFile(source);

        Assert.Equal("screenshot.png", attachment.DisplayName);
        Assert.StartsWith("attached-", attachment.FileName);
        Assert.NotEqual(attachment.FileName, attachment.DisplayName);
    }

    // ---- 1.0.1: per-attachment size display ----

    [Fact]
    public void AddFile_RecordsByteCount_AndFormatsForChip()
    {
        // Drop a 1.5 KB text file. ByteCount is the
        // size of the on-disk managed copy (what
        // the agent will see) and SizeDisplay
        // renders it the same way Finder /
        // Explorer would ("1.5 KB") so the user
        // can read the chip at a glance.
        var source = Path.Combine(_scratchDir, "notes.txt");
        File.WriteAllText(source, new string('x', 1500));

        var vm = new PendingAttachmentsViewModel();
        var attachment = vm.AddFile(source);

        Assert.Equal(1500, attachment.ByteCount);
        Assert.Equal("1.5 KB", attachment.SizeDisplay);
    }

    [Fact]
    public void AddFile_LargeFile_FormatsAsMB()
    {
        // 1.5 MB crosses the KB→MB threshold. The
        // formatter uses 1024-based binary units
        // (1 MB = 1024 KB) and labels them with
        // the SI suffix the OS file manager uses,
        // so 1.5 MB reads as expected.
        var source = Path.Combine(_scratchDir, "big.pdf");
        // 1.5 MiB of zeros — fast to write, exact
        // size, no edge cases from compression.
        var bytes = new byte[1024 * 1024 + 512 * 1024];
        File.WriteAllBytes(source, bytes);

        var vm = new PendingAttachmentsViewModel();
        var attachment = vm.AddFile(source);

        Assert.Equal(bytes.Length, attachment.ByteCount);
        Assert.Equal("1.5 MB", attachment.SizeDisplay);
    }

    [Fact]
    public void AddFile_VerySmallFile_FormatsAsBytes()
    {
        // Below 1 KB the formatter stays in bytes
        // (no "0.0 KB" rounding). A 200-byte file
        // reads as "200 B" — the exact size the
        // user would see in Finder.
        var source = Path.Combine(_scratchDir, "small.json");
        File.WriteAllText(source, "{ \"key\": \"value\" }");

        var vm = new PendingAttachmentsViewModel();
        var attachment = vm.AddFile(source);

        Assert.Equal(attachment.ByteCount, attachment.ByteCount);
        Assert.EndsWith(" B", attachment.SizeDisplay);
    }

    [Fact]
    public void AddFile_OneGigabyte_FormatsAsGB()
    {
        // GB threshold is rare in real drops but
        // the formatter should still land on the
        // right unit so a 2 GB video file reads
        // as "2 GB" and not "2048 MB".
        const long size = 2L * 1024 * 1024 * 1024;
        var source = Path.Combine(_scratchDir, "huge.bin");
        // Don't actually write 2 GB to disk — FileInfo
        // is what the VM reads, so a sparse file of
        // the right apparent size is enough.
        using (var fs = File.Create(source))
        {
            fs.SetLength(size);
        }

        var vm = new PendingAttachmentsViewModel();
        var attachment = vm.AddFile(source);

        Assert.Equal(size, attachment.ByteCount);
        Assert.Equal("2 GB", attachment.SizeDisplay);
    }
}
