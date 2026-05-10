using AIChat.Application.Artifacts;
using AIChat.Domain.Artifacts;

namespace AIChat.Tests.Artifacts;

public sealed class InputArtifactFileStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "AIChatArtifactStoreTests", Guid.NewGuid().ToString("N"));

    public InputArtifactFileStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task StoreAsync_CopiesFileAndRecordsStoredPath()
    {
        var sourcePath = Path.Combine(_tempDir, "source.png");
        await File.WriteAllTextAsync(sourcePath, "fake image bytes");
        var artifact = new InputArtifact
        {
            Id = "artifact-1",
            ProjectId = "project-1",
            FileName = "screen.png"
        };

        var store = new InputArtifactFileStore(Path.Combine(_tempDir, "managed"));
        await store.StoreAsync(artifact, sourcePath);

        Assert.True(artifact.Metadata.ContainsKey("storedPath"));
        Assert.True(File.Exists(artifact.Metadata["storedPath"]));
        Assert.Equal("fake image bytes", await File.ReadAllTextAsync(artifact.Metadata["storedPath"]));
        Assert.Contains("artifact-1.png", artifact.Metadata["storedPath"]);
        Assert.Contains("project-1", artifact.Metadata["storedRelativePath"]);
    }

    [Fact]
    public async Task DeleteStoredFile_DeletesOnlyManagedStoredPath()
    {
        var managedRoot = Path.Combine(_tempDir, "managed");
        var outsidePath = Path.Combine(_tempDir, "outside.txt");
        await File.WriteAllTextAsync(outsidePath, "keep me");
        var store = new InputArtifactFileStore(managedRoot);
        var artifact = new InputArtifact
        {
            Metadata = { ["storedPath"] = outsidePath }
        };

        store.DeleteStoredFile(artifact);

        Assert.True(File.Exists(outsidePath));
    }

    [Fact]
    public async Task DeleteStoredFiles_RemovesManagedCopies()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "content");
        var artifact = new InputArtifact
        {
            Id = "artifact-2",
            ProjectId = "project-1",
            FileName = "notes.txt"
        };
        var store = new InputArtifactFileStore(Path.Combine(_tempDir, "managed"));
        await store.StoreAsync(artifact, sourcePath);
        var storedPath = artifact.Metadata["storedPath"];

        store.DeleteStoredFiles([artifact]);

        Assert.False(File.Exists(storedPath));
    }

    [Fact]
    public async Task StoreBytesAsync_WritesManagedCopyAndRecordsStoredPath()
    {
        var artifact = new InputArtifact
        {
            Id = "artifact-bytes",
            ProjectId = "project-1",
            FileName = "optimized.jpg"
        };
        var store = new InputArtifactFileStore(Path.Combine(_tempDir, "managed"));

        await store.StoreBytesAsync(artifact, [1, 2, 3], "jpg");

        Assert.True(File.Exists(artifact.Metadata["storedPath"]));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(artifact.Metadata["storedPath"]));
        Assert.EndsWith("artifact-bytes.jpg", artifact.Metadata["storedPath"]);
        Assert.Contains("project-1", artifact.Metadata["storedRelativePath"]);
    }

    [Fact]
    public async Task DeleteProjectStore_RemovesManagedProjectDirectory()
    {
        var sourcePath = Path.Combine(_tempDir, "source.png");
        await File.WriteAllTextAsync(sourcePath, "image");
        var artifact = new InputArtifact
        {
            Id = "artifact-3",
            ProjectId = "project:with-invalid-name",
            FileName = "image.png"
        };
        var store = new InputArtifactFileStore(Path.Combine(_tempDir, "managed"));
        await store.StoreAsync(artifact, sourcePath);
        var storedDirectory = Path.GetDirectoryName(artifact.Metadata["storedPath"]);

        store.DeleteProjectStore(artifact.ProjectId);

        Assert.NotNull(storedDirectory);
        Assert.False(Directory.Exists(storedDirectory));
    }

    [Fact]
    public void DeleteProjectStore_IgnoresBlankProjectId()
    {
        var managedRoot = Path.Combine(_tempDir, "managed");
        Directory.CreateDirectory(managedRoot);
        var store = new InputArtifactFileStore(managedRoot);

        store.DeleteProjectStore("");

        Assert.True(Directory.Exists(managedRoot));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
