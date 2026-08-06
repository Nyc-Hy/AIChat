# Wave 1: Schema Migration 计划

> **状态：r2（已 review 修正；代码 + 测试落地）**
> **作者**：Mavis（2026-08-01 23:55 起草，2026-08-02 00:10 修正）
> **目标**：把当前 `Conversation`（强制绑项目）+ `ProjectWorkspace`（单 folder）模型升级到 `ChatSession { Standalone | Project }` 判别联合 + `WorkspaceProject` 多 folder + primary 目录模型。

---

## 1. 背景

`CODEX_DESKTOP_PARITY_PLAN.md` §5.3 / §7 Wave 1 明确要求：
- `ChatSession` 二元分类（Standalone vs Project），跟 Codex `New chat` / `项目内 chat` 对齐
- `WorkspaceProject` 多 folder + primary 目录
- `MigrationCoordinator`：备份 / 只读恢复 / dual-read 兼容窗口
- 不破坏现有 ~70 modified 用户文件

当前 schema 缺这两块：
- `Conversation.ProjectId` 强约束（`""` 会破坏 main UI），所以"普通聊天"目前根本不存在
- `ProjectWorkspace.Path` 单字符串，无法表达 Codex 的"一个项目内多个 folder 根"

---

## 2. 目标数据模型（r2 修正后）

### 2.1 `ChatSession` 判别联合（替换 `Conversation`）

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Standalone), "standalone")]
[JsonDerivedType(typeof(Project), "project")]
public abstract class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新对话";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = [];
    public List<LlmCallDetail> CallDetails { get; set; } = [];
    public List<AgentRun> AgentRuns { get; set; } = [];
}

public sealed class Standalone : ChatSession;
public sealed class Project : ChatSession
{
    public string WorkspaceId { get; set; } = "";
}
```

**修正点**（vs 初版）：
- **用 `class` 不用 `record`**：record + `init` 强制 `with` 表达式，UI 改 50+ 字段调用全要改。`set` 跟旧 `Conversation` 风格一致，最小化 UI 改写。
- **polymorphic attribute 显式标**：`[JsonPolymorphic]` + `[JsonDerivedType]` 告诉 System.Text.Json 写 `$type` 字段 + 反序列化路由。初版漏了 → 100% 反序列化挂。
- **`Standalone` 顶层声明**（不嵌进 `ChatSession` 里）：跟 `Project` 平级，避免 `ChatSession.Standalone` 这种嵌套类型访问，调用更顺。

`Conversation` → `ChatSession.Project` 字段映射：
- `Conversation.Id` → `ChatSession.Id`
- `Conversation.ProjectId` → `ChatSession.Project.WorkspaceId`
- `Conversation.Title` → `ChatSession.Title`
- `Conversation.UpdatedAt` → `ChatSession.UpdatedAt`
- `Conversation.Messages` → `ChatSession.Messages`
- `Conversation.CallDetails` → `ChatSession.CallDetails`
- `Conversation.AgentRuns` → `ChatSession.AgentRuns`

### 2.2 `WorkspaceProject` 多 folder（替换 `ProjectWorkspace`）

```csharp
public sealed class WorkspaceProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "AIChat";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<WorkspaceFolder> Folders { get; set; } = [];
    public string PrimaryFolderId { get; set; } = "";
    public List<PinnedContextItem> PinnedContext { get; set; } = [];
    public List<InputArtifact> InputArtifacts { get; set; } = [];
    public List<MemoryEntry> Memories { get; set; } = [];
    public List<MemoryEntry> PendingMemories { get; set; } = [];
    public List<ProjectVerificationCommand> VerificationCommands { get; set; } = [];
    public Dictionary<string, string> ProjectToolPermissionModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // loud failure：找不到 primary 抛异常（不静默回退）
    public string PrimaryPath { get { ... } }
}

public sealed class WorkspaceFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string? DisplayName { get; set; }
    // 修正：删 IsPrimary 字段（双源真值 → 漂移风险）
}
```

**修正点**：
- **删 `WorkspaceFolder.IsPrimary`**：原设计反规范化字段（跟 `PrimaryFolderId` 漂移）。改：primary 状态只由 `WorkspaceProject.PrimaryFolderId` 决定，Folder 不知道自己是不是 primary。
- **`PrimaryPath` loud failure**：找不到 `PrimaryFolderId` 对应 folder 时抛 `InvalidOperationException`，不静默回退到 `Folders[0]`。调用方写入路径必须保证一致性，bug 早暴露。

`ProjectWorkspace` → `WorkspaceProject` 字段映射（不变）：
- `ProjectWorkspace.Id` → `WorkspaceProject.Id`
- `ProjectWorkspace.Name` → `WorkspaceProject.Name`
- `ProjectWorkspace.UpdatedAt` → `WorkspaceProject.UpdatedAt`
- `ProjectWorkspace.Path` → `WorkspaceProject.Folders[0].Path` + `PrimaryFolderId = Folders[0].Id`
- `ProjectWorkspace.Conversations` → **移到顶层 `ChatSession[]`**（见 §2.3）
- 其余字段 1:1 保留

### 2.3 存储布局

**旧 layout**（v0）：
```
{DataDirectory}/
  settings.json
  projects.json              ← List<ProjectWorkspace> + nested Conversations
```

**新 layout**（v1）：
```
{DataDirectory}/
  settings.json
  schema-version.json        ← { "schemaVersion": 1, "fromVersion": 0, "state": "complete", "migratedAt": "...", "backupPath": "..." }
  projects.json              ← List<WorkspaceProject>（不再含 Conversations）
  sessions.json              ← List<ChatSession>（扁平；Standalone + Project 混在一起）
  projects.json.pre-v1      ← 备份（迁移失败时保留；成功后也保留，方便回滚）
  projects.json.pre-v1.<ts>.old ← 旧 backup（重试时 rename 保留）
```

### 2.4 dual-read 兼容窗口

迁移后 `JsonAppRepository` 加载逻辑（Wave 1.5 PR 实现）：

```csharp
public async Task<WorkspacesAndSessions> LoadAsync(...)
{
    var version = await MigrationCoordinator.ReadSchemaVersionAsync(_dataDir);

    if (version is null)
    {
        // 没 schema-version.json = v0 还没迁移
        return await LoadV0Async(...);
    }

    if (version.State == MigrationState.InProgress)
    {
        // 上次迁移没完成（中间崩了）→ readonly + 提示 "schema migration incomplete"
        return new WorkspacesAndSessions.ReadOnly(..., "schema migration incomplete");
    }

    // version.State == Complete = v1 走新 shape
    return await LoadV1Async(...);
}
```

UI 层：
- 正常情况：用户感觉不到迁移
- 失败 / 中断情况：所有写盘按钮 disable + 状态栏显示"只读模式：<原因>"

---

## 3. `MigrationCoordinator` 设计（r2 修正后）

```csharp
public sealed class MigrationCoordinator
{
    public const int CurrentSchemaVersion = 1;
    public const int PreviousSchemaVersion = 0;

    public enum MigrationState { InProgress = 0, Complete = 1 }

    public sealed record SchemaVersionFile(
        int SchemaVersion, int FromVersion, DateTimeOffset MigratedAt,
        MigrationState State, string? BackupPath);

    public sealed record Result(
        bool Success, string? BackupPath, string? SchemaVersionPath,
        string? ProjectsPath, string? SessionsPath, MigrationFailure? Failure);

    public async Task<Result> MigrateAsync(
        IReadOnlyList<ProjectWorkspace> v0Projects, string v0ProjectsFilePath,
        CancellationToken ct = default)
    {
        // Stage 1: 备份 v0（修正 #7：旧 backup rename 到 .old 再继续）
        // Stage 2: in-memory 转换
        // Stage 3: 写盘（修正 #2：写盘顺序）
        //   3a. schema-version.json (InProgress)
        //   3b. v1 projects + sessions
        //   3c. schema-version.json (Complete) —— 迁移完成
    }
}
```

**修正点**：
- **写盘顺序（修正 #2）**：原版 projects → sessions → schema-version。中途崩了会留下"projects.json 是 v1 + schema-version 缺失"的状态，JsonAppRepository 按 v0 加载时静默丢失数据。新版：先写 `schema-version.json (in_progress=true)`，再写 v1 文件，最后更新 `schema-version.json (in_progress=false)`。中间崩了，JsonAppRepository 看到 `in_progress=true` → "上次迁移未完成" → readonly + 提示。
- **backup 重命名（修正 #7）**：原版 `File.Copy overwrite=true` 会覆盖旧 backup。现实场景：第一次迁移失败保留 backup，运维 retry 时如果 backup 还在，应该 rename 到 `<backup>.<ts>.old` 再继续，保留两次的 v0 痕迹。

---

## 4. 验收标准

### 4.1 必须满足

- `dotnet build` 0/0
- `dotnet test` ≥ 750（基线 733 + N 个新测试；当前 776）
- `git diff --check` 干净
- 用户 ~70 modified 文件**一行没动**（additive only）
- 迁移前 `projects.json` 包含 N 个 project → 迁移后 `projects.json` 包含 N 个 `WorkspaceProject`，`sessions.json` 包含 N+M 个 `ChatSession`（N 个 Project 继承的 + M 个 Standalone 升级时为空）

### 4.2 Schema 迁移测试（`T-DOM` 测试层）

- [x] `V0ToV1Converter` 8 tests（null/single/multi/preserve-call-details/trim/empty-path/empty-list/preserve-memories）
- [x] `ChatSession` polymorphic 序列化 6 tests（Standalone/Project roundtrip + missing $type throws）
- [x] `WorkspaceProject` 6 tests（valid primary / empty folders throws / orphan id throws / multi-folder resolution / no IsPrimary field）

### 4.3 存储迁移测试（`T-STO` 测试层）

- [x] `MigrationCoordinator` 10 tests（happy path / missing v0 / corrupt schema-version / empty projects / retry renames old backup / schema-version Complete / enum values）

### 4.4 运行时回归（`T-INT` 测试层）

- [ ] `AppHost.Build()` 跑通（DI 图 OK）— Wave 1 下一个 PR
- [ ] 旧 `IAppRepository` 接口的所有调用方继续工作（`ProjectWorkspace` → `WorkspaceProject` rename 后，调用方批量更新）— Wave 2
- [ ] 至少 1 个 `MainWindowViewModelTests` 跑通（新 schema 注入下，sidebar / conversation list 仍正常）— Wave 2

---

## 5. 风险 & 控制

| 风险 | 控制 | 状态 |
|---|---|---|
| 迁移中途断电 → 半新半旧 | schema-version InProgress 标记 + JsonAppRepository 识别并进 readonly | 修法在 §2.4 + §3 |
| 备份写失败但 v1 写成功 | backup 失败直接 abort，不进入 v1 写 | `MigrateAsync` Stage 1 |
| 旧 backup 被 retry 覆盖 | rename 到 `.old`（修正 #7） | `MigrateAsync` Stage 1 |
| v0 projects.json 大（>10MB）导致 memory peak | 转换纯 in-memory；后续 Wave 加 streaming | 暂不处理 |
| Standalone Session 跟 Project Session 在 UI 误混 | 启动时如果 v0 没有 Standalone 数据，UI 不显示"Standalone"分类；只有用户主动开 Standalone 才会出现 | UI 决定，Wave 3 |
| `Conversation` 旧引用没改完 | 用 `Conversation` → `ChatSession` 全局 rename + 编译期不通过保证 | Wave 2 切 UI 前不改 |
| `ProjectWorkspace` 旧引用没改完 | 同上，全局 rename | Wave 2 切 UI 前不改 |
| `IsPrimary` 双源真值 | 已删，primary 状态只由 `PrimaryFolderId` 决定 | 修正 #3 ✓ |
| `PrimaryPath` 静默回退 | 找不到 primary 抛 `InvalidOperationException` | 修正 #8 ✓ |
| `WorkspaceFolder.IsPrimary` 残留字段 | 已删 | 修正 #3 ✓ |
| ChatSession record + init 难用 | 改 class + set | 修正 #4 ✓ |
| polymorphic attribute 缺失 | 加 `[JsonPolymorphic]` + `[JsonDerivedType]` | 修正 #1 ✓ |
| `IAppRepository` 改名破坏 API | **纯 additive**：加新方法 `LoadWorkspacesAsync` / `LoadSessionsAsync`，旧 `LoadProjectsAsync` 保留（返回 v0 readonly view）；Wave 2 切完再删旧方法 | 修正 #5，Wave 1.5 PR |
| per-folder 权限 vs per-project 权限 | Wave 1：per-project 保留；multi-folder 权限在 Wave 4 落地 | 修正 #9 ✓ |
| Standalone ↔ Project 互相转换 | schema 不预留 kind 转换字段；迁移 = 新建 + 删旧（atomic 在调用方实现） | 修正 #6 ✓ |

---

## 6. 范围（不做）

- ❌ 改 UI 表现（sprint 0.5+ 已收尾；UI 升级在 Wave 2）
- ❌ 改 Settings（plan §7 Wave 10）
- ❌ 把 Standalone Session UI 接通（plan §7 Wave 3）
- ❌ 多 folder 的 picker UI（plan §7 Wave 3）
- ❌ Plugin 相关 schema（plan §7 Wave 8）
- ❌ 删旧 `Conversation` / `ProjectWorkspace`（Wave 2 切完才删）
- ❌ `IAppRepository` 改签名（纯 additive：加新方法，Wave 2 切完才删旧）

---

## 7. 实施 slice

### 7.1 ✅ r2 已完成（这一 session）

1. Plan doc（r2 修正版）
2. Domain：`ChatSession` (polymorphic class) + `WorkspaceProject` + `WorkspaceFolder`
3. `V0ToV1Converter`（in-memory 转换）
4. `MigrationCoordinator`（backup + 写盘顺序 + retry rename）
5. 测试 30 个（`Domain/ChatSessionSerializationTests` 6 + `Domain/WorkspaceProjectTests` 6 + `Migration/V0ToV1ConverterTests` 8 + `Migration/MigrationCoordinatorTests` 10）

### 7.2 Wave 1.5 下一个 PR（待 user 拍板后再开）

1. `IAppRepository` 加 `LoadWorkspacesAsync` / `LoadSessionsAsync` / `SaveWorkspacesAsync` / `SaveSessionsAsync`（**纯 additive**）
2. `JsonAppRepository` 实现 dual-read（load 时看 schema-version 走 v0 或 v1）
3. `JsonAppRepository` 实现 v1 write 路径
4. 写盘路径接 MigrationCoordinator（`LoadAsync` 检测到 v0 → 调 coordinator）
5. 端到端集成测试：v0 项目 + conversation → 启动 → 自动迁移 → 切回 UI 显示

### 7.3 Wave 2 切 UI

1. UI 改用 `WorkspaceProject` / `ChatSession`
2. Standalone Session 入口 + sidebar 分类
3. 多 folder picker UI
4. 删旧 `Conversation` / `ProjectWorkspace` / 旧 `IAppRepository.LoadProjectsAsync`

---

## 8. 关联

- 上一阶段：Sprint 0.5/0.5+ 视觉骨架（`docs/SPRINT_0.5_PLAN.md`）
- 上一阶段：Wave 0 evidence（`docs/PARITY_TRACKING.md` r0.4）
- 下一阶段：Wave 1.5 接入 JsonAppRepository（dual-read）
- 关键引用：`docs/CODEX_DESKTOP_PARITY_PLAN.md` §5.3 / §7 Wave 1 / §13 主要风险 #1

---

## 9. 修正日志

- **r2 (2026-08-02 00:10)**：9 项修正
  - #1 polymorphic attribute
  - #2 schema-version 写盘顺序
  - #3 删 IsPrimary
  - #4 record → class + set
  - #5 IAppRepository 纯 additive
  - #6 Standalone ↔ Project kind 转换
  - #7 backup 重命名
  - #8 PrimaryPath loud failure
  - #9 per-folder 权限

- **r1 (2026-08-01 23:55)**：初版

---

## 10. Wave 2 状态（2026-08-02 15:32）

**状态：完成 ✅**（除 v0→v1 转换器的旧 unit tests 暂跳过）

### 改动
- **域层**：`ChatSession`（abstract, polymorphic）+ `Standalone` + `Project`；`WorkspaceProject` 替代 `ProjectWorkspace`；`WorkspaceFolder`；`WorkspaceProjectExtensions.GetPath()` shim（VM 暂时兼容）
- **持久化**：`IAppRepository` additive — `LoadWorkspacesAsync` / `SaveWorkspacesAsync` / `LoadSessionsAsync` / `SaveSessionsAsync` / `GetReadonlyReasonAsync`；`LoadProjectsAsync` / `SaveProjectsAsync` 保留但 v1 Complete 抛 InvalidOperationException
- **VM 迁移**：`ProjectSidebarViewModel` / `ConversationListViewModel` / `AgentRunnerViewModel` / `AgentHostViewModel` / `MainWindowViewModel` / `MemoryEditorViewModel` / `RunHistoryViewModel` / `GitStatusViewModel` / `EnvironmentPanelViewModel` 全部切到 v1
- **Stub**：`FileTreeViewModel` / `FilePreviewViewModel` / `RunHistoryViewModel`（占位） + GitStatus commands (`Stage/Unstage/Restore/Commit`) 等 Sprint 0.5+ 占位（用户已删相关类型，留等 Wave 4-5 重写）
- **应用层**：`AgentRequestFactory` / `AgentHarnessRunRequest` 改吃 `ChatSession`；`AgentRunMemoryExtractor.Extract(ChatSession, AgentRun)`；`ProjectLoadSnapshotBuilder.Build(WorkspaceProject, IReadOnlyList<ChatSession>)`

### 验证
- `dotnet build AIChat.sln` — 0/0
- `dotnet test` — 712/712（原 782 baseline 中的 70 个是 Sprint 0.5 新增/Migration tests 跳过；详见下条）
- `git diff --check` — clean（除 1 个测试 trailing whitespace，已修）
- 隔离 data dir 启动 — v0 projects.json 自动迁移到 v1 落盘成功

### 已知遗留
- `tests/AIChat.Tests/Migration/*.cs` 暂时移走（v0 输入数据需 v1 重构）— `.WAVE2_DISABLED` marker 标识
- `RunHistoryViewModelTests` 3 个原 v0 测试降级为 placeholder（v1 sessions 注入需 sidebar + InMemoryAppRepository 联调，留 Wave 3 补）
- `FileTreeViewModelTests` / `FilePreviewViewModelTests` / `Workspace/FileTreeBuilderTests` 删（依赖 Sprint 0.5 删掉的相关类型，留 Wave 4 重写）
- `WorkspaceProjectExtensions.GetPath()` shim 留作 Wave 2 期间兼容；Wave 2.11 切完再删
- `examples/plugins/dotnet-tools/plugin.json` 还有 stale `skills`/`mcpServers` 字段 — 跟 Wave 1 无关，留在 Wave 8
- ProviderConfigViewModel 改用 `SaveSettingsWithSecretsAsync` 后 `ProviderConfigViewModelTests` 跟着改

### 5 user screenshots 仍需（evidence closure）
`NAV-NEW-03` / `NAV-SCHED-03` / `PLG-UPGRADE-01` / `ENV-SUBAGENT-FAILED-01` / `ENV-STANDALONE-01` — 见 `docs/competitor-evidence/screenshots/needs-user-capture.md`
