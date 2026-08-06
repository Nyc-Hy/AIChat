using System.Text.Json;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Storage.Json;
using AIChat.Storage.Json.Migration;

// v0 types (ProjectWorkspace, Conversation) are [Obsolete] since Wave 3.
// These dual-read tests construct v0 data to verify the v0→v1 migration
// path; suppress CS0618 at the file level.
#pragma warning disable CS0618

namespace AIChat.Tests.Storage;

// T-INT layer: Wave 1.5 端到端 dual-read 验证。
// 真实 temp data dir，写 v0 projects.json，构造 JsonAppRepository，调
// v1 API（LoadWorkspacesAsync / LoadSessionsAsync）→ 触发 MigrationCoordinator →
// 自动迁移 → 拿到 v1 数据 + 磁盘上现在存 v1 文件 + schema-version.json 存在。
public sealed class JsonAppRepositoryDualReadTests : IDisposable
{
    private readonly string _dataDir;

    public JsonAppRepositoryDualReadTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "aichat-dualread", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadWorkspacesAsync_OnV0Disk_AutoMigratesAndReturnsV1Shape()
    {
        // 写 v0 disk state
        var v0Path = Path.Combine(_dataDir, "projects.json");
        var v0Project = NewV0Project("p1", "/tmp/repo", 2);
        await WriteV0Async(v0Path, new List<ProjectWorkspace> { v0Project });

        // 构造 repo + 调 v1 API
        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        var workspaces = await repo.LoadWorkspacesAsync();

        // v1 返回正确
        Assert.Single(workspaces);
        Assert.Equal("p1", workspaces[0].Id);
        Assert.Equal("/tmp/repo", workspaces[0].PrimaryPath);
        Assert.Single(workspaces[0].Folders);

        // 迁移后磁盘是 v1
        Assert.True(File.Exists(Path.Combine(_dataDir, "projects.json")));
        Assert.True(File.Exists(Path.Combine(_dataDir, "sessions.json")));
        Assert.True(File.Exists(Path.Combine(_dataDir, "schema-version.json")));

        // schema-version 是 Complete
        var version = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDir);
        Assert.NotNull(version);
        Assert.Equal(MigrationCoordinator.MigrationState.Complete, version!.State);
    }

    [Fact]
    public async Task LoadSessionsAsync_OnV0Disk_AutoMigratesAndReturnsV1Sessions()
    {
        var v0Path = Path.Combine(_dataDir, "projects.json");
        var v0Project = NewV0Project("p1", "/tmp", 3);
        await WriteV0Async(v0Path, new List<ProjectWorkspace> { v0Project });

        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        var sessions = await repo.LoadSessionsAsync();

        // 3 个 session，全是 Project kind 绑 p1
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, session =>
        {
            var project = Assert.IsType<Project>(session);
            Assert.Equal("p1", project.WorkspaceId);
        });
    }

    [Fact]
    public async Task LoadWorkspacesAsync_OnV1CompleteDisk_ReadsDirectlyWithoutReMigration()
    {
        // 先 migrate
        var v0Path = Path.Combine(_dataDir, "projects.json");
        await WriteV0Async(v0Path, new List<ProjectWorkspace> { NewV0Project("p1", "/tmp", 0) });
        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        await repo.LoadWorkspacesAsync();  // 触发 migrate

        // 然后再 LoadWorkspaces 第二次：不再重新迁移
        var backupPath = Path.Combine(_dataDir, "projects.json.pre-v1");
        var backupContentBefore = await File.ReadAllTextAsync(backupPath);

        // 改 v1 文件（模拟 UI 写）
        var modified = new List<WorkspaceProject>
        {
            new()
            {
                Id = "p1",
                Name = "Modified",
                Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/p1" }],
                PrimaryFolderId = "f1",
            },
        };
        await repo.SaveWorkspacesAsync(modified);

        // 再 Load
        var reloaded = await repo.LoadWorkspacesAsync();
        Assert.Equal("Modified", reloaded[0].Name);
        // backup 没被覆盖（说明没重跑迁移）
        var backupContentAfter = await File.ReadAllTextAsync(backupPath);
        Assert.Equal(backupContentBefore, backupContentAfter);
    }

    [Fact]
    public async Task LoadProjectsAsync_OnV1Disk_Throws()
    {
        // Wave 3: v0 API removed. This test now verifies the v1 API
        // works after migration (the v0 throws part is gone since the
        // method itself is gone — the only way to read v0 data is via
        // LoadWorkspacesAsync, which transparently migrates).
        var v0Path = Path.Combine(_dataDir, "projects.json");
        await WriteV0Async(v0Path, new List<ProjectWorkspace> { NewV0Project("p1", "/tmp", 0) });
        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());

        // v1 API 拿到迁移后的 workspace
        var workspaces = await repo.LoadWorkspacesAsync();
        Assert.Single(workspaces);
        Assert.Equal("p1", workspaces[0].Id);
    }

    [Fact]
    public async Task GetReadonlyReasonAsync_OnHealthyDisk_ReturnsNull()
    {
        // v0 disk 状态（没 schema-version）
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "projects.json"), "[]");

        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        var reason = await repo.GetReadonlyReasonAsync();
        Assert.Null(reason);
    }

    [Fact]
    public async Task GetReadonlyReasonAsync_OnInProgressSchemaVersion_ReturnsReason()
    {
        // 模拟上次迁移中断：写 schema-version (InProgress)
        var versionPath = Path.Combine(_dataDir, "schema-version.json");
        var inProgressFile = new MigrationCoordinator.SchemaVersionFile(
            1, 0, DateTimeOffset.UtcNow,
            MigrationCoordinator.MigrationState.InProgress,
            null);
        await File.WriteAllTextAsync(versionPath, JsonSerializer.Serialize(inProgressFile));

        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        var reason = await repo.GetReadonlyReasonAsync();
        Assert.NotNull(reason);
        Assert.Contains("migration incomplete", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveWorkspacesAsync_OnV1Disk_PersistsAcrossNewInstance()
    {
        // migrate
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "projects.json"), "[]");
        var repo1 = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        await repo1.LoadWorkspacesAsync();

        // 改 v1 状态
        var newWs = new List<WorkspaceProject>
        {
            new()
            {
                Id = "p1",
                Name = "P1",
                Folders = [new WorkspaceFolder { Id = "f1", Path = "/tmp/x" }],
                PrimaryFolderId = "f1",
            }
        };
        await repo1.SaveWorkspacesAsync(newWs);

        // 新实例读取
        var repo2 = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        var loaded = await repo2.LoadWorkspacesAsync();
        Assert.Single(loaded);
        Assert.Equal("P1", loaded[0].Name);
        Assert.Equal("/tmp/x", loaded[0].PrimaryPath);
    }

    [Fact]
    public async Task LoadWorkspacesAsync_OnEmptyDataDir_ReturnsEmpty()
    {
        // 空 data dir（用户首次启动）→ Load 返空，不报错
        var repo = new JsonAppRepository(_dataDir, new SessionOnlySecretProtector());
        var workspaces = await repo.LoadWorkspacesAsync();

        Assert.Empty(workspaces);
        // 空 data dir 不创建 backup（修正：仅当 v0 文件存在才备份）
        Assert.False(File.Exists(Path.Combine(_dataDir, "projects.json.pre-v1")));
        // 但 schema-version 写下来了（标记迁移"完成"）
        Assert.True(File.Exists(Path.Combine(_dataDir, "schema-version.json")));
    }

    private static ProjectWorkspace NewV0Project(string id, string path, int conversations)
    {
        var p = new ProjectWorkspace
        {
            Id = id,
            Name = id,
            Path = path,
        };
        for (var i = 0; i < conversations; i++)
        {
            p.Conversations.Add(new Conversation
            {
                Id = $"{id}-c{i}",
                ProjectId = id,
                Title = $"{id} conv {i}",
            });
        }
        return p;
    }

    private static async Task WriteV0Async(string path, List<ProjectWorkspace> projects)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, projects);
    }
}
