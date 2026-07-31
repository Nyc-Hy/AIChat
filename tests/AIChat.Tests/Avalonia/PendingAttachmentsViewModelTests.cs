using System.IO;
using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// Unit tests for the pending-attachments strip. The full paste
// pipeline (clipboard read → bitmap save → thumbnail decode →
// add to collection) needs a real GUI to exercise end-to-end, so
// these tests cover the contract surface that doesn't require a
// real Bitmap: collection state, null-safety, and the file
// cleanup contract.
public class PendingAttachmentsViewModelTests : IDisposable
{
    public void Dispose()
    {
        // Best-effort cleanup of any temp files the tests created.
        try
        {
            if (Directory.Exists(PendingAttachmentsViewModel.StorageDirectory))
            {
                foreach (var file in Directory.GetFiles(PendingAttachmentsViewModel.StorageDirectory, "pasted-*.png"))
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
}
