using AIChat.Application.Workspace;
using Moq;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceCommitBatchRunnerTests
{
    [Fact]
    public async Task CommitAsync_MultipleFiles_PassesAllPathsToService()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "Update 2 files",
            It.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "a.cs", "b.cs" })),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceCommitResult { Message = "Update 2 files" });

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "b.cs", Status = " M" }
        };

        var result = await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "Update 2 files", changes);

        Assert.Equal("Update 2 files", result.Message);
        service.Verify();
    }

    [Fact]
    public async Task CommitAsync_DuplicatePaths_DeduplicatesByOrdinalIgnoreCase()
    {
        IReadOnlyList<string> capturedPaths = null!;
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "Update 2 files",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<string>, CancellationToken>((_, _, paths, _) => capturedPaths = paths)
            .ReturnsAsync(new WorkspaceCommitResult { Message = "Update 2 files" });

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "A.cs", Status = " M" },
            new() { Path = "b.cs", Status = " M" }
        };

        await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "Update 2 files", changes);

        Assert.Equal(2, capturedPaths!.Count);
        Assert.Contains("a.cs", capturedPaths);
        Assert.Contains("b.cs", capturedPaths);
    }

    [Fact]
    public async Task CommitAsync_Message_PassesThroughUnchanged()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "my custom message",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceCommitResult { Message = "my custom message" });

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" }
        };

        var result = await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "my custom message", changes);

        Assert.Equal("my custom message", result.Message);
    }

    [Fact]
    public async Task CommitAsync_EmptyChanges_PassesEmptyPathsToService()
    {
        IReadOnlyList<string> capturedPaths = null!;
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "Update 0 files",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<string>, CancellationToken>((_, _, paths, _) => capturedPaths = paths)
            .ReturnsAsync(new WorkspaceCommitResult { Message = "Update 0 files" });

        var changes = new List<WorkspaceChange>();

        await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "Update 0 files", changes);

        Assert.Empty(capturedPaths!);
    }

    [Fact]
    public async Task CommitAsync_ServiceThrows_PropagatesToCaller()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "Update 1 file",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("git failed"));

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkspaceCommitBatchRunner.CommitAsync(
                service.Object, "/proj", "Update 1 file", changes));

        Assert.Contains("git failed", ex.Message);
    }

    [Fact]
    public async Task CommitSingleAsync_PathPassedAsSingleElementList()
    {
        var capturedPaths = default(IReadOnlyList<string>);
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "Update file.cs",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<string>, CancellationToken>((_, _, paths, _) => capturedPaths = paths)
            .ReturnsAsync(new WorkspaceCommitResult { Message = "Update file.cs" });

        var result = await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "Update file.cs", "file.cs");

        Assert.Single(capturedPaths!);
        Assert.Equal("file.cs", capturedPaths![0]);
    }

    [Fact]
    public async Task CommitSingleAsync_MessagePassedThroughUnchanged()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "my message",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceCommitResult { Message = "my message" });

        var result = await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "my message", "a.cs");

        Assert.Equal("my message", result.Message);
    }

    [Fact]
    public async Task CommitSingleAsync_ResultReturnedUnchanged()
    {
        var service = new Mock<IWorkspaceChangeService>();
        var expectedResult = new WorkspaceCommitResult
        {
            Commit = "abc123",
            Message = "Update a.cs",
            Paths = new List<string> { "a.cs" }
        };
        service.Setup(s => s.CommitAsync(
            "/proj", "Update a.cs",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await WorkspaceCommitBatchRunner.CommitAsync(
            service.Object, "/proj", "Update a.cs", "a.cs");

        Assert.Equal("abc123", result.Commit);
        Assert.Equal("Update a.cs", result.Message);
    }

    [Fact]
    public async Task CommitSingleAsync_ExceptionPropagatesToCaller()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.CommitAsync(
            "/proj", "Update a.cs",
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("git failed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkspaceCommitBatchRunner.CommitAsync(
                service.Object, "/proj", "Update a.cs", "a.cs"));

        Assert.Contains("git failed", ex.Message);
    }
}
