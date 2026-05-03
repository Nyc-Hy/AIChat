using AIChat.Application.Workspace;
using Moq;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceStageBatchRunnerTests
{
    [Fact]
    public async Task StageAsync_PathsDeduplicated_ReturnsCount()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.StageAsync(
            "/proj", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "A.cs", Status = " M" },
            new() { Path = "b.cs", Status = " M" }
        };

        var result = await WorkspaceStageBatchRunner.StageAsync(
            service.Object, "/proj", changes);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task StageAsync_EmptyList_ReturnsZeroCount()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.StageAsync(
            "/proj", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await WorkspaceStageBatchRunner.StageAsync(
            service.Object, "/proj", new List<WorkspaceChange>());

        Assert.Equal(0, result.Count);
        service.Verify(s => s.StageAsync(
            "/proj", It.Is<IReadOnlyList<string>>(p => p.Count == 0), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task StageAsync_ServiceThrows_PropagatesToCaller()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.StageAsync(
            "/proj", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("git failed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkspaceStageBatchRunner.StageAsync(
                service.Object, "/proj",
                new List<WorkspaceChange> { new() { Path = "a.cs", Status = " M" } }));

        Assert.Contains("git failed", ex.Message);
    }

    [Fact]
    public async Task UnstageAsync_OnlyStagedChanges_PassesFilteredPaths()
    {
        List<string>? capturedPaths = null;
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.UnstageAsync(
            "/proj", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken>((_, paths, _) => capturedPaths = paths.ToList())
            .Returns(Task.CompletedTask);

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "b.cs", Status = "M " },
            new() { Path = "c.cs", Status = " M" }
        };

        var result = await WorkspaceStageBatchRunner.UnstageAsync(
            service.Object, "/proj", changes);

        Assert.Equal(1, result.Count);
        Assert.Single(capturedPaths!);
        Assert.Equal("b.cs", capturedPaths[0]);
    }

    [Fact]
    public async Task UnstageAsync_NoStagedChanges_ReturnsZeroCount()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.UnstageAsync(
            "/proj", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var changes = new List<WorkspaceChange>
        {
            new() { Path = "a.cs", Status = " M" },
            new() { Path = "b.cs", Status = " M" }
        };

        var result = await WorkspaceStageBatchRunner.UnstageAsync(
            service.Object, "/proj", changes);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task UnstageAsync_ServiceThrows_PropagatesToCaller()
    {
        var service = new Mock<IWorkspaceChangeService>();
        service.Setup(s => s.UnstageAsync(
            "/proj", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("git failed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkspaceStageBatchRunner.UnstageAsync(
                service.Object, "/proj",
                new List<WorkspaceChange> { new() { Path = "a.cs", Status = "M " } }));

        Assert.Contains("git failed", ex.Message);
    }
}
