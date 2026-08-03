using System.Text.Json;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;
using AIChat.Domain.Projects;
using AIChat.Storage.Json.Migration;

// v0 types (ProjectWorkspace, Conversation) are [Obsolete] since Wave 3.
// The migration tests are the one place that still construct them, so
// we suppress CS0618 at the file level.
#pragma warning disable CS0618

namespace AIChat.Tests.Migration;

// T-MIG-MC layer: 端到端 v0→v1 文件迁移的集成测试。
// 覆盖 plan §7.1 (MigrationCoordinatorTests 10 个) + 修正 #2 / 修正 #6 / 修正 #7:
//   修正 #2: schema-version 先写 in_progress=true,再写 v1,最后 in_progress=false
//   修正 #6: 迁移失败时保留 v0 + backup(不破坏原数据)
//   修正 #7: 已有 backup 时 rename 到 .<ts>.old 再继续
//
// 测试用临时目录隔离;每个 test 在自己的 data root 跑,互不污染。
public sealed class MigrationCoordinatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dataDir;
    private readonly string _v0ProjectsFile;

    public MigrationCoordinatorTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "AIChatMigrationCoordinatorTests",
            Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_tempRoot, "AIChat");
        _v0ProjectsFile = Path.Combine(_dataDir, "projects.json");
        Directory.CreateDirectory(_dataDir);
        // Pre-create the v0 projects file so the coordinator's backup
        // stage has something to copy from. Without this file the
        // backup step is skipped (File.Copy guarded by File.Exists)
        // and Result.BackupPath ends up null. The contents are dummy
        // — the migration reads the v0 list from the in-memory
        // argument, not from this file.
        File.WriteAllText(_v0ProjectsFile, "{\"SchemaVersion\":0,\"Projects\":[]}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static ProjectWorkspace MakeV0Project(string id = "p1", string path = "/tmp/repo")
        => new()
        {
            Id = id,
            Name = "AIChat",
            Path = path,
            Conversations = [new Conversation { Id = "c1", ProjectId = id, Title = "first" }],
        };

    [Fact]
    public async Task MigrateAsync_SingleProject_WritesAllV1Files()
    {
        var coordinator = new MigrationCoordinator(_dataDir);
        var v0 = new List<ProjectWorkspace> { MakeV0Project() };

        var result = await coordinator.MigrateAsync(v0, _v0ProjectsFile);

        Assert.True(result.Success);
        Assert.NotNull(result.SchemaVersionPath);
        Assert.NotNull(result.ProjectsPath);
        Assert.NotNull(result.SessionsPath);
        Assert.True(File.Exists(result.SchemaVersionPath!));
        Assert.True(File.Exists(result.ProjectsPath!));
        Assert.True(File.Exists(result.SessionsPath!));
    }

    [Fact]
    public async Task MigrateAsync_BackupsV0FileToPreV1()
    {
        // 修正 #6: 备份必须在 v1 写盘之前,这样如果迁移失败,原数据还在
        await File.WriteAllTextAsync(_v0ProjectsFile, "{\"SchemaVersion\":0,\"Projects\":[]}");
        var coordinator = new MigrationCoordinator(_dataDir);

        var result = await coordinator.MigrateAsync(
            [MakeV0Project()],
            _v0ProjectsFile);

        Assert.True(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        // backup 内容跟原 v0 一致
        var backupContent = await File.ReadAllTextAsync(result.BackupPath);
        Assert.Contains("\"SchemaVersion\":0", backupContent);
    }

    [Fact]
    public async Task MigrateAsync_WhenV0FileMissing_StillMigratesWithoutBackup()
    {
        // v0 文件不存在(测试场景:repo 拿到 v0 数据但 file 路径被改)
        // 备份阶段跳过,但 v1 写盘继续
        File.Delete(_v0ProjectsFile);
        var coordinator = new MigrationCoordinator(_dataDir);
        var v0 = new List<ProjectWorkspace> { MakeV0Project() };

        var result = await coordinator.MigrateAsync(v0, _v0ProjectsFile);

        Assert.True(result.Success);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public async Task MigrateAsync_ExistingBackup_RenamesToTimestampedOld()
    {
        // 修正 #7: 重试场景,旧 backup 不被覆盖,先 rename 到 .<ts>.old
        await File.WriteAllTextAsync(_v0ProjectsFile, "v0-new");
        var backupPath = _v0ProjectsFile + ".pre-v1";
        await File.WriteAllTextAsync(backupPath, "v0-old");

        var coordinator = new MigrationCoordinator(_dataDir);
        var result = await coordinator.MigrateAsync(
            [MakeV0Project()],
            _v0ProjectsFile);

        Assert.True(result.Success);
        // 新 backup 是 v0-new 内容
        var newBackup = await File.ReadAllTextAsync(backupPath);
        Assert.Equal("v0-new", newBackup);
        // 旧 backup 被 rename 到 .<ts>.old
        var parent = Path.GetDirectoryName(backupPath)!;
        var oldBackups = Directory.GetFiles(parent, "projects.json.pre-v1.*.old");
        Assert.Single(oldBackups);
        var renamedContent = await File.ReadAllTextAsync(oldBackups[0]);
        Assert.Equal("v0-old", renamedContent);
    }

    [Fact]
    public async Task MigrateAsync_SchemaVersionFile_HasCompleteState()
    {
        // 修正 #2: 迁移完成的 schema-version state=Complete,不是 InProgress
        var coordinator = new MigrationCoordinator(_dataDir);

        var result = await coordinator.MigrateAsync(
            [MakeV0Project()],
            _v0ProjectsFile);

        Assert.True(result.Success);
        var versionFile = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDir);
        Assert.NotNull(versionFile);
        Assert.Equal(1, versionFile!.SchemaVersion);
        Assert.Equal(0, versionFile.FromVersion);
        Assert.Equal(MigrationCoordinator.MigrationState.Complete, versionFile.State);
        Assert.NotNull(versionFile.BackupPath);
    }

    [Fact]
    public async Task MigrateAsync_EmptyV0List_WritesValidV1Files()
    {
        // 边界:空 v0 list 也应该写出合法的 v1 文件(schema-version + 空 projects + 空 sessions)
        var coordinator = new MigrationCoordinator(_dataDir);

        var result = await coordinator.MigrateAsync([], _v0ProjectsFile);

        Assert.True(result.Success);
        // v1 projects + sessions 都是 []
        var projectsJson = await File.ReadAllTextAsync(result.ProjectsPath!);
        var sessionsJson = await File.ReadAllTextAsync(result.SessionsPath!);
        Assert.Contains("[]", projectsJson);
        Assert.Contains("[]", sessionsJson);
    }

    [Fact]
    public async Task ReadSchemaVersionAsync_MissingFile_ReturnsNull()
    {
        // 没有任何 schema-version 文件 → null(JsonAppRepository 会走 v0 加载路径)
        var versionFile = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDir);
        Assert.Null(versionFile);
    }

    [Fact]
    public async Task ReadSchemaVersionAsync_MalformedJson_ReturnsNull()
    {
        // 文件存在但 JSON 损坏 → null(不抛),让 JsonAppRepository fallback 到 v0
        var schemaPath = Path.Combine(_dataDir, "schema-version.json");
        await File.WriteAllTextAsync(schemaPath, "this is not json {{{");

        var versionFile = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDir);

        Assert.Null(versionFile);
    }

    [Fact]
    public async Task MigrateAsync_ResultContainsExpectedFilePaths()
    {
        // Result 字段路径正确,host 可以直接拿去做下一步(比如 UI 提示用户 backup 在哪)
        var coordinator = new MigrationCoordinator(_dataDir);

        var result = await coordinator.MigrateAsync(
            [MakeV0Project()],
            _v0ProjectsFile);

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(_dataDir, "schema-version.json"), result.SchemaVersionPath);
        Assert.Equal(Path.Combine(_dataDir, "projects.json"), result.ProjectsPath);
        Assert.Equal(Path.Combine(_dataDir, "sessions.json"), result.SessionsPath);
        Assert.Equal(_v0ProjectsFile + ".pre-v1", result.BackupPath);
    }

    [Fact]
    public async Task MigrateAsync_PreservesPinnedContextAcrossDiskRoundtrip()
    {
        // 端到端:写盘后能从 v1 projects.json 读回 PinnedContext(确保 JsonSerializer
        // 对 polymorphic 类型的 round-trip 正确)
        var v0Project = MakeV0Project();
        v0Project.PinnedContext = [new PinnedContextItem { Id = "ctx", Path = "AGENTS.md" }];

        var coordinator = new MigrationCoordinator(_dataDir);
        var result = await coordinator.MigrateAsync([v0Project], _v0ProjectsFile);

        Assert.True(result.Success);
        // Re-read v1 from disk to verify the round-trip (the JSON uses
        // camelCase by default per JsonSerializerDefaults.Web)
        var projectsJson = await File.ReadAllTextAsync(result.ProjectsPath!);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<List<WorkspaceProject>>(
            projectsJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(roundTripped);
        var ws = Assert.Single(roundTripped!);
        Assert.Single(ws.PinnedContext);
        Assert.Equal("AGENTS.md", ws.PinnedContext[0].Path);
    }
}
