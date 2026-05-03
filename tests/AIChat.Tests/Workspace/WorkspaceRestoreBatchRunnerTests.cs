using AIChat.Application.Workspace;
using Moq;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceRestoreBatchRunnerTests
{
    [Fact]
    public async Task RestoreAsync_AllSucceed_RestoredEqualsCount()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.RestoreFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRestoreResult { Restored = true });

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "b.cs", Status = " M" }
        };

        var result = await WorkspaceRestoreBatchRunner.RestoreAsync(service.Object, "/proj", changes);

        Assert.Equal(2, result.Restored);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task RestoreAsync_PartialFailure_ContinuesAndRecordsErrors()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.SetupSequence(s => s.RestoreFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRestoreResult { Restored = true })
            .ThrowsAsync(new InvalidOperationException("disk error"))
            .ReturnsAsync(new WorkspaceRestoreResult { Restored = true });

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "b.cs", Status = " M" },
            new() { Path = "c.cs", Status = " M" }
        };

        var result = await WorkspaceRestoreBatchRunner.RestoreAsync(service.Object, "/proj", changes);

        Assert.Equal(2, result.Restored);
        Assert.Single(result.Errors);
        Assert.Contains("b.cs", result.Errors[0]);
        Assert.Contains("disk error", result.Errors[0]);
    }

    [Fact]
    public async Task RestoreAsync_UntrackedFile_PassesDeleteUntrackedTrue()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.RestoreFileAsync(
            "/proj", "new.cs", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRestoreResult { DeletedUntracked = true })
            .Verifiable();

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "new.cs", Status = "??" }
        };

        await WorkspaceRestoreBatchRunner.RestoreAsync(service.Object, "/proj", changes);

        service.Verify();
    }

    [Fact]
    public async Task RestoreAsync_TrackedFile_PassesDeleteUntrackedFalse()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.RestoreFileAsync(
            "/proj", "modified.cs", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRestoreResult { Restored = true })
            .Verifiable();

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "modified.cs", Status = " M" }
        };

        await WorkspaceRestoreBatchRunner.RestoreAsync(service.Object, "/proj", changes);

        service.Verify();
    }

    [Fact]
    public async Task RestoreAsync_EmptyList_ReturnsZeroRestoredNoErrors()
    {
        var service = new Mock<IWorkspaceChangeService>();
        var changes = new List<WorkspaceChange>();

        var result = await WorkspaceRestoreBatchRunner.RestoreAsync(service.Object, "/proj", changes);

        Assert.Equal(0, result.Restored);
        Assert.Empty(result.Errors);
        service.Verify(s => s.RestoreFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}
