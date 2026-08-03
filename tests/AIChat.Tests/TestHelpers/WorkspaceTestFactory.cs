using AIChat.Domain.Projects;

namespace AIChat.Tests.TestHelpers;

// Wave 2 测试 helper:把 v0 "Path = X" 投影到 v1 Folders/PrimaryFolderId。
// 让 v0 风格的测试代码最小改动就能编 —— 旧 ProjectWorkspace + Path 仍工作。
public static class WorkspaceTestFactory
{
    // 单一 folder 的 WorkspaceProject,fid 固定,path 跟参数。
    public static WorkspaceProject MakeWorkspace(string id, string name, string path)
    {
        var folderId = "f1";
        return new WorkspaceProject {
            Id = id,
            Name = name,
            Folders = [new WorkspaceFolder { Id = folderId, Path = path }],
            PrimaryFolderId = folderId,
        };
    }

    // 接受完整配置,免得调用方写 5 行 boilerplate。
    public static WorkspaceProject MakeWorkspace(
        string id,
        string name,
        string path,
        List<WorkspaceFolder>? folders = null,
        string? primaryFolderId = null)
    {
        var actualFolders = folders ?? [new WorkspaceFolder { Id = "f1", Path = path }];
        return new WorkspaceProject {
            Id = id,
            Name = name,
            Folders = actualFolders,
            PrimaryFolderId = primaryFolderId ?? actualFolders[0].Id,
        };
    }
}
