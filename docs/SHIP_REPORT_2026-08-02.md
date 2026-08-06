# AIChat Wave 11 Ship Report — 2026-08-02

> Codex Desktop 操作对等项目的 12-wave first-slice 实施报告。
> 提交人：Mavis（自托管 session）
> 起点 commit：项目 user-side 70+ modified files, 0 docs (plan/track 均为 untracked)
> 终点：712 tests → 798 tests (+86), 12 waves 全部 first-slice ship + Wave 11 review fix pass, P0 三件全过。

## 1. P0 Release Gate 验证

| Gate | 命令 | 结果 |
|---|---|---|
| 编译干净 | `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` | ✅ 0 警告 0 错误 |
| 测试全过 | `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` | ✅ 817/817 pass (32s) |
| diff --check | `git diff --check` | ✅ empty |
| AppHost DI | `AppHostTests.Build_ResolvesTopLevelService` | ✅ 36/36 (+1 for `IBackgroundProcessSupervisor` in Wave 7 follow-up) |
| 迁移幂等 | `MigrationCoordinatorTests`, `V0ToV1ConverterTests`, `JsonAppRepositoryDualReadTests` | ✅ 19/19 (v0→v1 round-trip + backup rename + corrupted JSON) |
| Registry 持久化 | `PluginRegistryTests`, `ScheduledTaskRegistryTests`, `SiteRegistryTests`, `BackgroundProcessSupervisorTests` | ✅ 30/30 (load / add / update / remove / set-enabled / record-run / start / stop / spawn-failure / restart-recovery / log-tail) |
| App 启动 | `AICHAT_ISOLATED_DATA_ROOT=$(mktemp -d) dotnet run --project src/AIChat.App.Avalonia` | ✅ ALIVE=yes (8s 内启动,主窗口 + 5 first-level nav 全部启用) |

## 2. 12 Wave First-Slice Ship 状态

| Wave | 范围 | 状态 | 关键交付 |
|---|---|---|---|
| 0 | 文档权威 + 竞品证据 | ✅ 完整 | 4 旧 plan doc 加 banner 指向 CODEX_DESKTOP_PARITY_PLAN.md,PARITY_TRACKING r0.4,8 个 user 真机截图 + 16 官方 URL 核验 |
| 1 | Session + Environment + 持久化迁移 | ✅ First slice | ChatSession polymorphic (Standalone/Project), WorkspaceProject 多 folder, V0ToV1Converter + MigrationCoordinator, 8+10 tests |
| 2 | v0→v1 实际迁移 | ✅ First slice | 双读 + 写时升级路径,8+10 迁移测试通过 |
| 3 | Standalone Session + 多 folder | ✅ First slice | sidebar Standalone section + ProjectCardViewModel.SyncFolders + MultiFolderBadge "📁 N" + 设为主 |
| 4 | Composer `+` 菜单 | ✅ First slice | 6 项 MenuFlyout (添加文件/图片/引用/剪贴板/网页搜索/插件) + 模型 selector + 完全访问 toggle |
| 5 | 5 first-level nav + Environment 5 sections | ✅ First slice | 新对话/拉取请求/站点/已安排/插件 + 变更/本地/子智能体/来源/Background sections |
| 6 | Git 真实 Stage/Unstage/Restore/Commit | ✅ First slice | GitStatusViewModel.StageSelected 等接入 IWorkspaceChangeService,StageError 红色横幅 + LastCommitDisplay |
| 7 | Subagent per-run list + Sources 真实化 + Background section 隐藏 | ✅ First slice | EnvironmentPanelViewModel.SubAgentRuns 镜像 + SubAgentRunViewModel.StatusBrush 状态色板 + Sources list 真实化 + ShowBackgroundProcesses=false |
| 7.1 | **BackgroundProcessSupervisor (plan §13 P0 "整个子进程树")** | ✅ **Wave 7 follow-up ship (2026-08-02 22:35)** | `BackgroundProcess` domain + `IBackgroundProcessSupervisor` + 进程组杀 (P/Invoke setpgid + kill -pid) + 重启恢复 (Running→Crashed if PID dead) + 日志 tail 捕获 (200 lines ring buffer) + Environment panel 镜像 (ShowBackgroundProcesses=true) + Sites 真实本地预览 (python3 -m http.server) + 9 supervisor tests + 4 panel wiring tests + DI lock |
| 8 | Plugin registry + Plugins modal | ✅ First slice | IPluginRegistry + PluginRegistry (持久化 .state.json) + PluginsView + Plugins nav 入口启用 |
| 9 | Scheduled + Sites 模态 | ✅ First slice | IScheduledTaskRegistry/ISiteRegistry + JsonFileStore 共享 helper + ScheduledView (暂停/恢复/立即运行/删除) + SitesView (预览/停止/删除,云部署按 plan §5.4 禁用) |
| 10 | Settings 4 大分类 + 搜索 | ✅ First slice | SettingsCategory enum (Personal/Integrations/Coding/Archived) + SearchText 跨分类过滤 + ThemePreference 接入 + SettingsView 2-column 布局 |
| 11 | 对等验收 + 发布 | ✅ First slice | 本报告 (P0 gate 全过 + 各 wave 状态表 + deferred items 清单) |

## 3. 关键文件路径速查

### 文档
- `docs/CODEX_DESKTOP_PARITY_PLAN.md` — 单一权威 plan (12 waves + P0/P1/P2 gates)
- `docs/PARITY_TRACKING.md` — Feature → Journey → Evidence → Test 版本化追踪表 (r0.11)
- `docs/SHIP_REPORT_2026-08-02.md` — 本文件
- `docs/PROJECT_HANDOFF.md` — 280 行 handoff
- `docs/competitor-evidence/` — 8+ 张用户真机截图 + 16 官方 URL 核验报告
- `art/` — AIChat UI 渲染图

### 域模型
- `src/AIChat.Domain/Chat/ChatSession.cs` — polymorphic session
- `src/AIChat.Domain/Projects/WorkspaceProject.cs` + `WorkspaceFolder.cs` — 多 folder project
- `src/AIChat.Domain/Scheduled/ScheduledTask.cs` — Wave 9 数据模型
- `src/AIChat.Domain/Sites/Site.cs` — Wave 9 数据模型

### 服务层
- `src/AIChat.Application/Plugins/PluginRegistry.cs` + `IPluginRegistry.cs`
- `src/AIChat.Application/Scheduled/ScheduledTaskRegistry.cs` + `IScheduledTaskRegistry.cs`
- `src/AIChat.Application/Sites/SiteRegistry.cs` + `ISiteRegistry.cs`
- `src/AIChat.Application/Persistence/JsonFileStore.cs` — 原子写 + corruption recovery
- `src/AIChat.Storage.Json/Migration/V0ToV1Converter.cs` + `MigrationCoordinator.cs`

### UI 层
- `src/AIChat.App.Avalonia/ViewModels/MainWindowViewModel.cs` — host,OpenPlugins/Scheduled/Sites 模态
- `src/AIChat.App.Avalonia/ViewModels/EnvironmentPanelViewModel.cs` — SubAgentRuns mirror
- `src/AIChat.App.Avalonia/ViewModels/PluginsViewModel.cs` + `SitesViewModel.cs` + `ScheduledViewModel.cs`
- `src/AIChat.App.Avalonia/ViewModels/SettingsViewModel.cs` — 4 大分类 + 搜索
- `src/AIChat.App.Avalonia/ViewModels/SubAgentRunViewModel.cs` — StatusBrush 状态色板
- `src/AIChat.App.Avalonia/Views/Controls/{Plugins,Scheduled,Sites,Settings,EnvironmentPanel}View.axaml`
- `src/AIChat.App.Avalonia/Composition/ServiceRegistration.cs` — DI 接入 (IPluginRegistry/IScheduledTaskRegistry/ISiteRegistry + 6 个 modal VMs)

### 测试
- 35 AppHost tests (含 6 个 DI lock for Wave 8/9)
- 19 Migration tests (v0→v1 round-trip + 备份)
- 21 Registry tests (Plugin/Scheduled/Site)
- 8 ModalListViewModel VM tests (command → registry routing for Plugins/Scheduled/Sites)
- 13 Sources/Background Hide tests
- 11 Settings category + search tests
- 7 StatusBrush palette tests
- 2 MainWindowModalGuard tests (modals stay closed + CloseAllModals drops every modal)

## 4. Deferred Items 清单 (需要 follow-up PR)

按 plan §7 Wave 11 + §10 P1 退出条件整理:

### P0 (阻塞合并/发布)
- **无 P0 项** — 当前状态满足 plan §10 P0 所有要求

### P1 (进入 Parity Beta 前必做)
- **Sub-agent 停止/取消/重定向** (Wave 7) — 需要 `AgentHarness.CancelSubAgentAsync` registry (per-sub-agent CancellationTokenSource)
- **BackgroundProcessSupervisor** (Wave 7) — 进程树 kill / log tail / restart recovery / 跨平台 (200+ 行)
- **Sources 真实化 (web search + clipboard polling + connector)** (Wave 7) — 300+ 行
- **Plugin install/uninstall + capability grants + trust chain** (Wave 8) — install 流程 + 数字签名 + 权限沙箱
- **真实本地预览 + 云部署 adapter** (Wave 9) — 需先有 BackgroundProcessSupervisor
- **真实 cron 调度引擎** (Wave 9) — 每日 09:00 真实触发,处理 no-human-interaction approval 失败
- **Settings 全页 Route** (Wave 10) — 当前是 modal,Codex 是全页
- **Settings 12 个 H2 章节完整实现** (Wave 10) — 智能快照/钩子/工作树/连接/环境 等
- **Computer Use 验收矩阵** (Wave 11) — 完整跑过 12 个用户旅程的截屏证据
- **跨平台真机 smoke** (Wave 11) — Windows / Linux 真机测试
- **键盘-only / 焦点 / AutomationProperties** (Wave 11) — a11y 审计
- **真实 Provider smoke** (Wave 11) — 至少 2 个真实 Provider 端到端跑通

### P2 (Stable 前必做)
- 性能预算验证 (启动 < 2s, 切换会话 < 200ms)
- 内存 / 订阅 / 后台进程泄漏审计
- Light/Dark 视觉回归
- 安装 / 升级 / 回滚 / 帮助 文档完整

### 不做 (plan §5.4 明确)
- **Browser extensions / 电脑操控** — AIChat 不集成远程浏览器
- **Cloud account / 计费** — AIChat 无云账户,凭据由 OS Keychain 持有
- **Pet / 宠物** — AIChat 不做装饰元素
- **第三方 Hosting Provider** — 本地预览是 user 主用场景

## 5. Known Test Gap (用户决策项)

以下 3 个测试文件被用户从仓库删除:

- `tests/AIChat.Tests/Avalonia/FilePreviewViewModelTests.cs`
- `tests/AIChat.Tests/Avalonia/FileTreeViewModelTests.cs`
- `tests/AIChat.Tests/Workspace/FileTreeBuilderTests.cs`

对应的源 VM (`FilePreviewView.cs`, `FileTreeView.cs`, `FileTreeBuilder.cs`) 也已删除。
恢复需要 user 决策 (源是 user 主动删除的,不应自动恢复)。

## 6. 关键设计决策

- **2-toggle permission model** (`DefaultAccess` + `FullAccessEnabled`) — 与 Codex Desktop 一致
- **AppRuntimeProfile.IsIsolated** — 干净 profile 给 UI test / demo 用
- **ChatSession polymorphic** — `Standalone` / `Project` 走同一基类
- **Wave 1.5 dual-read** — 旧 `ProjectWorkspace` / `Conversation` 数据自动迁移到 v1
- **Plugin state 持久化** — `.state.json` sidecar 在 plugins 目录下,key = plugin id
- **Settings 4 大分类 + 跨分类搜索** — 关键词集合每个 section 一组,搜索激活时跨分类显示
- **Background Processes 按 plan §7.7 严格隐藏** — supervisor 未建前不得展示入口 (`ShowBackgroundProcesses=false`)
- **Sites 云部署按 plan §5.4 严格禁用** — 无 Hosting Provider 时隐藏 (button `IsEnabled=False` + tooltip 解释依赖)

## 7. 验证命令速查

```bash
# P0 三件
dotnet build AIChat.sln --no-restore -m:1 -v:minimal
dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal
git diff --check

# 关键子系统
dotnet test --filter "FullyQualifiedName~AppHostTests"        # DI 35
dotnet test --filter "FullyQualifiedName~Migration"            # 19
dotnet test --filter "FullyQualifiedName~PluginRegistryTests"  # 8
dotnet test --filter "FullyQualifiedName~ScheduledTaskRegistryTests|FullyQualifiedName~SiteRegistryTests"  # 13
dotnet test --filter "FullyQualifiedName~EnvironmentPanelViewModelTests"  # 16
dotnet test --filter "FullyQualifiedName~SettingsViewModelCategoryTests"  # 11
dotnet test --filter "FullyQualifiedName~SubAgentRunViewModelTests"  # 17 (含 7 StatusBrush palette)

# 干净隔离启动
TMP_ROOT=$(mktemp -d)
env AICHAT_ISOLATED_DATA_ROOT="$TMP_ROOT" dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj --no-build -v:quiet > /tmp/aichat.log 2>&1 &
APP_PID=$!
sleep 8
ps -p $APP_PID > /dev/null && echo "ALIVE=yes"
pkill -9 -f "AIChat.App.Avalonia"
rm -rf "$TMP_ROOT" /tmp/aichat.log
```

## 8. 截图证据

| 文件 | 内容 |
|---|---|
| `docs/competitor-evidence/screenshots/2026-08-01-codex-main-view.png` | Codex Desktop 主界面 (用户真机,作为 parity target) |
| `docs/competitor-evidence/screenshots/2026-08-01-codex-settings-general.png` | Codex Desktop Settings 界面 (用户真机) |
| `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-aichat.png` | AIChat Sprint 0.5 状态 (3 栏布局 + 5 first-level nav) |
| `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-plus.png` | Sprint 0.5+ (sidebar top trio + permission badge 红→琥珀) |
| `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-post-fix.png` | Sprint 0.5 post-fix (空状态 bug 修复) |
| `docs/competitor-evidence/screenshots/2026-08-01-handoff.png` | handoff 时点状态 |
| `docs/competitor-evidence/screenshots/2026-08-02-wave7-sources-bg-hidden.png` | Wave 7 (Sources 真实化 + Background section 隐藏) |
| `docs/competitor-evidence/screenshots/2026-08-02-wave8-plugins-nav.png` | Wave 8 (Plugins nav 入口启用) |
| `docs/competitor-evidence/screenshots/2026-08-02-wave9-sites-scheduled.png` | Wave 9 (5 first-level nav 全部启用) |
| `docs/competitor-evidence/screenshots/2026-08-02-wave11-final-launch.png` | Wave 11 收尾 (全功能跑通) |

## 9. 不可控 / 外部依赖

- **macOS screencapture** — 截整屏 (含其他窗口),用户真机截图才精确
- **Computer Use** — agent 无访问权,Computer Use 验收矩阵需 user 跑
- **Windows / Linux 真机** — agent 仅在 macOS 验证
- **真实 Provider** — 需要 user 配置 (API key)

## 10. 总结

**12 wave first-slice 全部 ship + Wave 7 follow-up (BackgroundProcessSupervisor) ship,P0 release gate 全过。**
**测试基线 712 → 817 (+105 from 12 wave first-slice + Wave 7 follow-up)。**
**未 commit / 未 push (按 user "Do NOT commit" 规则)。**
**所有 deferred items 清晰登记,可由 follow-up PR 推进 P1/P2。**

下一步建议:
1. User 真机跑一次完整 Computer Use 验收矩阵
2. User 配置真实 Provider,跑一遍端到端聊天
3. User 决定是否 commit + push 这一批变更
4. 推进 P1 deferred items (推荐顺序: 1. 真实 cron 调度 → 2. Subagent 停止/取消 → 3. Settings 全页 Route → 4. Windows job objects process-tree kill)
