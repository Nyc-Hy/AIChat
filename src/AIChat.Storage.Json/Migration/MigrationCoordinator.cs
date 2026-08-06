using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

// v0 types (ProjectWorkspace) are [Obsolete] since Wave 3. The migration
// coordinator is the bridge that reads v0 files off disk and hands them
// to V0ToV1Converter — it's the one place that must still take a
// ProjectWorkspace argument, so we suppress CS0618 at the file level.
#pragma warning disable CS0618

namespace AIChat.Storage.Json.Migration;

// Wave 1: schema migration coordinator（plan §3 + 修正 #2 #7）。
//
// 职责：拿 v0 数据的内存表示 + v0 文件路径，按原子顺序写 v1 三个文件
// (schema-version / projects / sessions)；写失败时保留 v0 + 备份。
//
// 修正 #2：写盘顺序 —— 之前是 projects → sessions → schema-version
// （schema-version 最后写）。但中间崩了会留下"projects.json 是 v1 + schema-version
// 缺失"的状态，JsonAppRepository 按 v0 加载时静默丢失数据。
// 新顺序：先写 schema-version.json(in_progress=true) 标记"迁移开始"，
// 然后写 v1 projects + sessions，最后更新 schema-version.json(in_progress=false)
// 标记"迁移完成"。如果中间崩，JsonAppRepository 看到 in_progress=true →
// 上次迁移未完成 → 进 readonly + 提示。
//
// 修正 #7：backup 重命名 —— 之前 File.Copy overwrite=true 会覆盖旧 backup。
// 现实场景：第一次迁移失败后保留 backup；运维手动 retry 时如果 backup 还在，
// 我们要把它 rename 到 <backup>.<ts>.old 再继续（保留两次的 v0 痕迹）。
//
// 本类只做"v0 → v1 一次性转换 + 写盘"。dual-read 窗口由 JsonAppRepository
// 自己负责（load 时根据 schema-version 决定走 v0 加载路径还是 v1 加载路径）。
public sealed class MigrationCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public const int CurrentSchemaVersion = 1;
    public const int PreviousSchemaVersion = 0;

    public enum MigrationState
    {
        InProgress = 0,
        Complete = 1,
    }

    public sealed record SchemaVersionFile(
        int SchemaVersion,
        int FromVersion,
        DateTimeOffset MigratedAt,
        MigrationState State,
        string? BackupPath);

    public sealed record Result(
        bool Success,
        string? BackupPath,
        string? SchemaVersionPath,
        string? ProjectsPath,
        string? SessionsPath,
        MigrationFailure? Failure);

    public sealed record MigrationFailure(string Stage, string Message, Exception? Cause);

    private readonly string _dataDirectory;

    public MigrationCoordinator(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public async Task<Result> MigrateAsync(
        IReadOnlyList<ProjectWorkspace> v0Projects,
        string v0ProjectsFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(v0Projects);
        ArgumentException.ThrowIfNullOrEmpty(v0ProjectsFilePath);

        var backupPath = v0ProjectsFilePath + ".pre-v1";
        var schemaPath = Path.Combine(_dataDirectory, "schema-version.json");
        var projectsPath = Path.Combine(_dataDirectory, "projects.json");
        var sessionsPath = Path.Combine(_dataDirectory, "sessions.json");

        // Stage 1: 备份 v0（备份失败直接 abort，不进入 v1 写）
        // 修正 #7：如果 backup 已存在（重试场景），把它 rename 到 .old 再继续
        string? actualBackupPath = null;
        if (File.Exists(v0ProjectsFilePath))
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    var existingBackupOld = $"{backupPath}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.old";
                    File.Move(backupPath, existingBackupOld);
                }
                File.Copy(v0ProjectsFilePath, backupPath, overwrite: false);
                actualBackupPath = backupPath;
            }
            catch (Exception ex)
            {
                return new Result(false, null, null, null, null,
                    new MigrationFailure("backup", $"备份 v0 失败: {ex.Message}", ex));
            }
        }

        // Stage 2: in-memory 转换
        V0ToV1Converter.Converted converted;
        try
        {
            converted = V0ToV1Converter.Convert(v0Projects);
        }
        catch (Exception ex)
        {
            return new Result(false, actualBackupPath, null, null, null,
                new MigrationFailure("convert", $"v0→v1 转换失败: {ex.Message}", ex));
        }

        // Stage 3: 写盘（修正 #2：先写 schema-version in_progress=true，
        // 然后 v1 projects + sessions，最后更新 schema-version in_progress=false）
        try
        {
            Directory.CreateDirectory(_dataDirectory);

            // 3a. 写 schema-version.json (in_progress=true)
            var versionFileInProgress = new SchemaVersionFile(
                CurrentSchemaVersion,
                PreviousSchemaVersion,
                DateTimeOffset.UtcNow,
                MigrationState.InProgress,
                actualBackupPath);
            await AtomicWriteJsonAsync(schemaPath, versionFileInProgress, cancellationToken);

            // 3b. 写 v1 projects + sessions
            await AtomicWriteJsonAsync(projectsPath, converted.WorkspaceProjects, cancellationToken);
            await AtomicWriteJsonAsync(sessionsPath, converted.Sessions, cancellationToken);

            // 3c. 更新 schema-version.json (in_progress=false) —— 迁移完成
            var versionFileComplete = versionFileInProgress with { State = MigrationState.Complete };
            await AtomicWriteJsonAsync(schemaPath, versionFileComplete, cancellationToken);
        }
        catch (Exception ex)
        {
            return new Result(false, actualBackupPath, null, projectsPath, sessionsPath,
                new MigrationFailure("write", $"v1 写盘失败: {ex.Message}", ex));
        }

        return new Result(true, actualBackupPath, schemaPath, projectsPath, sessionsPath, null);
    }

    private static async Task AtomicWriteJsonAsync<T>(string filePath, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    public static async Task<SchemaVersionFile?> ReadSchemaVersionAsync(string dataDirectory, CancellationToken cancellationToken = default)
    {
        var schemaPath = Path.Combine(dataDirectory, "schema-version.json");
        if (!File.Exists(schemaPath))
        {
            return null;
        }
        try
        {
            await using var stream = File.OpenRead(schemaPath);
            return await JsonSerializer.DeserializeAsync<SchemaVersionFile>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
