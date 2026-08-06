using AIChat.Domain.Projects;

namespace AIChat.Tests.Domain;

// T-DOM layer: WorkspaceProject 派生属性 + 一致性约束。
// 覆盖 plan 修正 #3 (删 IsPrimary) + 修正 #8 (PrimaryPath loud failure)。
public sealed class WorkspaceProjectTests
{
    [Fact]
    public void PrimaryPath_WithValidPrimaryId_ReturnsFolderPath()
    {
        var folderId = "f1";
        var ws = new WorkspaceProject {
            Folders = [new WorkspaceFolder { Id = folderId, Path = "/tmp/repo" }],
            PrimaryFolderId = folderId,
        };

        Assert.Equal("/tmp/repo", ws.PrimaryPath);
    }

    [Fact]
    public void PrimaryPath_EmptyFolders_Throws()
    {
        var ws = new WorkspaceProject {
            Id = "ws-1",
            Name = "Empty",
            PrimaryFolderId = "f1", // 漂移: 没 folder 但 id 非空
        };

        Assert.Throws<InvalidOperationException>(() => ws.PrimaryPath);
    }

    [Fact]
    public void PrimaryPath_PrimaryIdDoesNotMatchAnyFolder_Throws()
    {
        var ws = new WorkspaceProject {
            Id = "ws-1",
            Name = "Stale",
            Folders = [new WorkspaceFolder { Id = "f2", Path = "/tmp/primary" }],
            PrimaryFolderId = "f1", // 漂移
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ws.PrimaryPath);
        Assert.Contains("f1", ex.Message);
        Assert.Contains("f2", ex.Message);
    }

    [Fact]
    public void PrimaryPath_MultipleFolders_PicksByPrimaryId()
    {
        var ws = new WorkspaceProject {
            Folders =
            [
                new WorkspaceFolder { Id = "f1", Path = "/tmp/secondary" },
                new WorkspaceFolder { Id = "f2", Path = "/tmp/primary" },
            ],
            PrimaryFolderId = "f2",
        };

        Assert.Equal("/tmp/primary", ws.PrimaryPath);
    }

    [Fact]
    public void Folders_DefaultToEmptyList()
    {
        var ws = new WorkspaceProject { Id = "ws-1", Name = "Fresh" };

        Assert.Empty(ws.Folders);
    }
}
