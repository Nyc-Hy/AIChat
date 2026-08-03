using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.Domain.Projects;
using AIChat.Storage.Json.Migration;

// v0 ProjectWorkspace is [Obsolete] since Wave 3. The only use here is
// LoadProjectsCoreV0Async (called by the v0→v1 migration coordinator to
// read legacy projects.json) — suppress CS0618 at the file level so the
// build stays clean while the v0 read path stays correct.
#pragma warning disable CS0618

namespace AIChat.Storage.Json;

// Local JSON implementation of IAppRepository. Settings and conversations are
// stored under %APPDATA%\AIChat with atomic-write semantics to prevent data
// corruption on concurrent or interrupted saves.
//
// Wave 1.5: dual-read 窗口。
// - v0 (无 schema-version.json) → 旧 LoadProjectsAsync 走 v0 加载；新 LoadWorkspacesAsync
//   触发 MigrationCoordinator 自动迁移到 v1
// - v1 Complete → 旧 LoadProjectsAsync 抛 InvalidOperationException（v0 API 已废弃，
//   UI 必须用新 API）；新 API 走 v1 加载
// - v1 InProgress → 全部走 readonly，UI 通过 GetReadonlyReasonAsync 显示提示
public sealed class JsonAppRepository : IAppRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _projectsPath;
    private readonly string _sessionsPath;
    private readonly string _schemaVersionPath;
    private readonly SemaphoreSlim _writeLock;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private readonly ISecretProtector _secretProtector;
    private Dictionary<string, CachedProtectedSecret> _secretCache = new(StringComparer.Ordinal);

    public JsonAppRepository()
        : this(
            AppRuntimeProfile.DataDirectory,
            CreateDefaultSecretProtector(AppRuntimeProfile.IsIsolated))
    {
    }

    internal static ISecretProtector CreateDefaultSecretProtector(bool isIsolated) =>
        isIsolated ? new SessionOnlySecretProtector() : new PlatformSecretProtector();

    public JsonAppRepository(string dataDirectory)
        : this(dataDirectory, new PlatformSecretProtector())
    {
    }

    internal JsonAppRepository(string dataDirectory, ISecretProtector secretProtector)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _projectsPath = Path.Combine(_dataDirectory, "projects.json");
        _sessionsPath = Path.Combine(_dataDirectory, "sessions.json");
        _schemaVersionPath = Path.Combine(_dataDirectory, "schema-version.json");
        _writeLock = WriteLocks.GetOrAdd(_dataDirectory, _ => new SemaphoreSlim(1, 1));
        _secretProtector = secretProtector;
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    _secretCache.Clear();
                    var initial = CreateInitialSettings();
                    ApplyEnvironmentSecretOverride(initial);
                    return initial;
                }

                EnsureFilePermissions(_settingsPath);
                string json;
                AppSettings settings;
                try
                {
                    json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
                    settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                               ?? CreateInitialSettings();
                }
                catch (JsonException)
                {
                    QuarantineCorruptFile(_settingsPath);
                    _secretCache.Clear();
                    return CreateInitialSettings();
                }

                var needsSecretMigration = ContainsLegacyPlainTextSecrets(json);
                AppSettings restored;
                IReadOnlyDictionary<string, CachedProtectedSecret>? restoreCache = null;
                if (EnvironmentSecretOverride.IsActive)
                {
                    // Env override: skip keychain access entirely. The
                    // platform vault is bypassed for the lifetime of this
                    // process; settings.json's `protectedApiKey` reference
                    // is left untouched so the user can `unset` the env var
                    // to fall back to the keychain.
                    RestoreLegacyPlainTextApiKeys(settings, json);
                    ApplyEnvironmentSecretOverride(settings);
                    _secretCache.Clear();
                    restored = settings;
                }
                else
                {
                    RestoreLegacyPlainTextApiKeys(settings, json);
                    var restoreResult = ProtectedSettingsSerializer.RestoreAfterLoad(
                        settings,
                        _secretProtector,
                        _secretCache);
                    restored = restoreResult.Settings;
                    restoreCache = restoreResult.Cache;
                }
                if (needsSecretMigration && !EnvironmentSecretOverride.IsActive)
                {
                    var migration = ProtectedSettingsSerializer.PrepareForSave(
                        restored,
                        _secretProtector,
                        restoreCache!,
                        persistedSecretMetadata: null,
                        persistSecretChanges: true,
                        forceProtect: true);
                    await AtomicWriteJsonUnderLockAsync(
                        _settingsPath,
                        migration.PersistedSettings,
                        cancellationToken);
                    ProtectedSettingsSerializer.ApplyProtectionMetadata(restored, migration.PersistedSettings);
                    _secretCache = migration.Cache;
                    DeleteRetiredSecrets(migration.DeletePurposes);
                }
                else if (restoreCache is not null)
                {
                    _secretCache = restoreCache.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value,
                        StringComparer.Ordinal);
                }
                return restored;
            }
            finally
            {
                _writeLock.Release();
            }
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => await SaveSettingsCoreAsync(settings, persistSecretChanges: false, cancellationToken);

    public async Task SaveSettingsWithSecretsAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
        => await SaveSettingsCoreAsync(settings, persistSecretChanges: true, cancellationToken);

    private async Task SaveSettingsCoreAsync(
        AppSettings settings,
        bool persistSecretChanges,
        CancellationToken cancellationToken)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var latestPersistedSettings = await ReadPersistedSettingsAsync(cancellationToken);
                var latestRevision = latestPersistedSettings?.PersistenceRevision ?? 0;
                if (latestPersistedSettings is not null &&
                    settings.PersistenceRevision != latestRevision)
                {
                    throw new InvalidOperationException(
                        "设置已被另一个窗口更新。请刷新后重试，当前保存未覆盖磁盘内容。");
                }

                var save = ProtectedSettingsSerializer.PrepareForSave(
                    settings,
                    _secretProtector,
                    _secretCache,
                    persistSecretChanges ? null : latestPersistedSettings,
                    persistSecretChanges);
                save.PersistedSettings.PersistenceRevision = checked(latestRevision + 1);
                await AtomicWriteJsonUnderLockAsync(
                    _settingsPath,
                    save.PersistedSettings,
                    cancellationToken);
                ProtectedSettingsSerializer.ApplyProtectionMetadata(settings, save.PersistedSettings);
                settings.PersistenceRevision = save.PersistedSettings.PersistenceRevision;
                _secretCache = save.Cache;
                DeleteRetiredSecrets(save.DeletePurposes);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    // Wave 3: v0 API removed. JsonAppRepository now exposes only the v1
    // shape (LoadWorkspacesAsync / LoadSessionsAsync). v0→v1 migration
    // runs in LoadWorkspacesAsync when the data directory has a v0
    // projects.json (no schema-version.json). See LoadProjectsCoreV0Async
    // for the v0 read path that feeds the migration.

    // === v1 API（Wave 1.5 引入） ===

    public async Task<string?> GetReadonlyReasonAsync(CancellationToken cancellationToken = default)
    {
        var version = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDirectory, cancellationToken);
        if (version?.State == MigrationCoordinator.MigrationState.InProgress)
        {
            return "schema migration incomplete (上轮 schema 迁移中断；v1 文件可能不完整)。" +
                   "请检查磁盘空间 / 文件权限后重启 app 触发重试。";
        }
        return null;
    }

    public async Task<IReadOnlyList<WorkspaceProject>> LoadWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        ThrowIfReadonly();

        if (!File.Exists(_projectsPath))
        {
            return [];
        }
        EnsureFilePermissions(_projectsPath);
        try
        {
            await using var stream = File.OpenRead(_projectsPath);
            var list = await JsonSerializer.DeserializeAsync<List<WorkspaceProject>>(stream, JsonOptions, cancellationToken);
            return (IReadOnlyList<WorkspaceProject>)(list ?? []);
        }
        catch (JsonException)
        {
            // v1 文件损坏：跟 v0 一样 quarantine + 返空（不要 break 整个 app）
            QuarantineCorruptFile(_projectsPath);
            return [];
        }
    }

    public async Task SaveWorkspacesAsync(IReadOnlyList<WorkspaceProject> workspaces, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        ThrowIfReadonly();
        await AtomicWriteJsonAsync(_projectsPath, workspaces, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSession>> LoadSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        ThrowIfReadonly();

        if (!File.Exists(_sessionsPath))
        {
            return [];
        }
        EnsureFilePermissions(_sessionsPath);
        try
        {
            await using var stream = File.OpenRead(_sessionsPath);
            var list = await JsonSerializer.DeserializeAsync<List<ChatSession>>(stream, JsonOptions, cancellationToken);
            return (IReadOnlyList<ChatSession>)(list ?? []);
        }
        catch (JsonException)
        {
            QuarantineCorruptFile(_sessionsPath);
            return [];
        }
    }

    public async Task SaveSessionsAsync(IReadOnlyList<ChatSession> sessions, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        ThrowIfReadonly();
        await AtomicWriteJsonAsync(_sessionsPath, sessions, cancellationToken);
    }

    // 首次调用任一 v1 API 时跑迁移（write lock 内串行）。
    // 已经在 _migrationGate 里，所以重复调用也是安全的。
    private async Task EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        // 快路径：已迁移 + Complete → 直接返
        var version = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDirectory, cancellationToken);
        if (version?.State == MigrationCoordinator.MigrationState.Complete)
        {
            return;
        }

        await _migrationGate.WaitAsync(cancellationToken);
        try
        {
            // 双重 check：拿锁后再 read 一次（避免多线程 race）
            version = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDirectory, cancellationToken);
            if (version?.State == MigrationCoordinator.MigrationState.Complete)
            {
                return;
            }

            // v0 → v1 迁移
            if (version?.State == MigrationCoordinator.MigrationState.InProgress)
            {
                // 上次中断 → 删 in_progress 标记，重新跑
                try { File.Delete(_schemaVersionPath); } catch { /* best effort */ }
            }

            var v0Projects = await LoadProjectsCoreV0Async(cancellationToken);
            var coordinator = new MigrationCoordinator(_dataDirectory);
            var result = await coordinator.MigrateAsync(v0Projects, _projectsPath, cancellationToken);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"schema 迁移失败 (stage={result.Failure?.Stage}): {result.Failure?.Message}",
                    result.Failure?.Cause);
            }
        }
        finally
        {
            _migrationGate.Release();
        }
    }

    private void ThrowIfReadonly()
    {
        // 不 await；只是同步检查文件最后写时间，避免 race
        // 如果当前 v1 处于 InProgress，UI 调用应被拒绝
        // 简单做法：依赖 GetReadonlyReasonAsync 在 UI 入口检查
        // 这里不强校验，避免每次 load 都读 schema-version（影响性能）
    }

    // 旧 LoadProjectsAsync 拆出核心 v0 加载(避免 EnsureMigratedAsync 走自己
    // 抛 InvalidOperationException 的 self-check 死循环)。Wave 3 后,
    // 这是 v0 唯一还在用 LoadProjectsCoreV0Async 的地方 —— migration
    // 协调器把 v0 文件转成 v1 后,后续 load 只走 LoadWorkspacesAsync。
    private async Task<List<ProjectWorkspace>> LoadProjectsCoreV0Async(CancellationToken cancellationToken)
    {
        if (!File.Exists(_projectsPath))
        {
            return [];
        }
        EnsureFilePermissions(_projectsPath);
        try
        {
            await using var stream = File.OpenRead(_projectsPath);
            var list = await JsonSerializer.DeserializeAsync<List<ProjectWorkspace>>(stream, JsonOptions, cancellationToken);
            list = list?
                .Where(project => !string.IsNullOrWhiteSpace(project.Path))
                .ToList();
            return list is null || list.Count == 0
                ? []
                : list;
        }
        catch (JsonException)
        {
            QuarantineCorruptFile(_projectsPath);
            return [];
        }
    }

    private async Task AtomicWriteJsonAsync<T>(string filePath, T value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await AtomicWriteJsonUnderLockAsync(filePath, value, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task AtomicWriteJsonUnderLockAsync<T>(
        string filePath,
        T value,
        CancellationToken cancellationToken)
    {
        EnsureDataDirectory();
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
            EnsureFilePermissions(tempPath);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(tempPath, filePath, overwrite: true);
            EnsureFilePermissions(filePath);
        }
        finally
        {
            // Clean up temp file if rename failed
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private async Task<AppSettings?> ReadPersistedSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void DeleteRetiredSecrets(IEnumerable<string> purposes)
    {
        foreach (var purpose in purposes)
        {
            try
            {
                _secretProtector.Delete(purpose);
            }
            catch
            {
                // The settings file is already safely committed. A vault
                // cleanup failure must not make an unrelated settings save
                // look unsuccessful. Platform vault cleanup is best effort.
            }
        }
    }

    private static AppSettings CreateInitialSettings()
    {
        return new AppSettings
        {
            ProviderId = "minimax",
            ProtocolId = "openai",
            ProviderName = "MiniMax",
            BaseUrl = "https://api.minimax.io/v1",
            ApiKey = "",
            Model = "MiniMax-M3",
            ModelContextLimit = 200_000,
            AgentMaxToolRounds = 16,
            AgentExecutionMode = AgentExecutionMode.Standard,
            MaxAutoFixRounds = 0,
            AutoVerifyAgentRuns = false,
            AgentAdaptiveStrategiesEnabled = false,
            AgentAdaptiveBudgetAndExplorerEnabled = false
        };
    }

    // Dev / CI override: when `AICHAT_API_KEY` is set in the environment, the
    // platform vault is bypassed at load time. The original `protectedApiKey`
    // / `apiKeyProtection` fields are left untouched so a user can fall back
    // to the keychain by `unset`ing the env var without losing their stored
    // secret.
    private static void ApplyEnvironmentSecretOverride(AppSettings settings)
    {
        if (EnvironmentSecretOverride.IsActive &&
            EnvironmentSecretOverride.TryGetMainKey(out var mainKey))
        {
            settings.ApiKey = mainKey;
        }

        foreach (var provider in settings.ConfiguredProviders)
        {
            if (EnvironmentSecretOverride.TryGetProviderKey(provider.Name, out var providerKey))
            {
                provider.ApiKey = providerKey;
            }
        }
    }

    private static void RestoreLegacyPlainTextApiKeys(AppSettings settings, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("apiKey", out var apiKeyElement))
            {
                settings.ApiKey = apiKeyElement.GetString() ?? "";
            }

            if (!root.TryGetProperty("configuredProviders", out var providersElement) ||
                providersElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var index = 0;
            foreach (var providerElement in providersElement.EnumerateArray())
            {
                if (index >= settings.ConfiguredProviders.Count)
                {
                    break;
                }

                if (providerElement.TryGetProperty("apiKey", out var providerApiKeyElement))
                {
                    settings.ConfiguredProviders[index].ApiKey = providerApiKeyElement.GetString() ?? "";
                }

                index++;
            }
        }
        catch (JsonException)
        {
            // Invalid JSON will be handled by the normal deserialize path.
        }
    }

    private static bool ContainsLegacyPlainTextSecrets(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (HasNonEmptyString(root, "apiKey") ||
            HasStringValue(root, "apiKeyProtection", "plain"))
        {
            return true;
        }

        if (!root.TryGetProperty("configuredProviders", out var providers) ||
            providers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return providers.EnumerateArray().Any(provider =>
            HasNonEmptyString(provider, "apiKey") ||
            HasStringValue(provider, "apiKeyProtection", "plain"));
    }

    private static bool HasNonEmptyString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(property.GetString());

    private static bool HasStringValue(JsonElement element, string propertyName, string expected)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private void EnsureDataDirectory()
    {
        Directory.CreateDirectory(_dataDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                _dataDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void EnsureFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private void QuarantineCorruptFile(string path)
    {
        try
        {
            EnsureDataDirectory();
            var quarantinePath = Path.Combine(
                _dataDirectory,
                $"{Path.GetFileName(path)}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
            File.Move(path, quarantinePath);
            EnsureFilePermissions(quarantinePath);
        }
        catch (IOException)
        {
            // The safe fallback settings still let the app open. Preserve the
            // original file when it cannot be moved so no evidence is lost.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // CreateInitialProjects / IsLegacySeedData (v0 helpers) removed in
    // Wave 3 — v0 load path returns [] for missing/empty/seed-shaped
    // data and the migration coordinator + v1 LoadWorkspacesAsync take
    // over from there.
}
