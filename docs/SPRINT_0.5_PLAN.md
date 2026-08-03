# Sprint 0.5：三栏主壳 + Environment 面板 + 5 入口 + 权限 Badge

> **状态：✅ 已完成（2026-08-01 22:35 跑通）+ Sprint 0.5+ 视觉 polish（2026-08-01 23:25）**
> 在 Wave 1 schema 迁移前，先做一段**可见的纵向切片**让用户能 evaluate 重构方向。
> 这是 §7 Wave 2 的 partial slice（不完整，但能跑）+ §4 一小段 decision 落实。
> 完成后用户**看效果再决定**是继续 Sprint 0.5 还是直接进 Wave 1。

## Sprint 0.5+ polish（在 0.5 完成后追加的视觉对齐）

| 改动 | 文件 | 用途 |
|---|---|---|
| Sidebar 顶 "AIChat ▽" 模式切换器按钮 | `MainWindow.axaml` + `MainWindow.axaml.cs` | 对齐 Codex 顶部"Codex ▽"模式切换 |
| 🔍 搜索图标 + 🔔 通知图标 | `MainWindow.axaml` + handlers | 对齐 Codex 顶 nav 3 件组（Wave 3 / Wave 7 才接真功能） |
| 全局样式 `Button.sidebar-brand` / `Button.sidebar-icon-button` | `App.axaml` | 配合新按钮 |
| 权限 badge 红 → 琥珀（`ErrorFgBrush` → `WarningFgBrush`） | `App.axaml` | 对齐 Codex "完全访问" 琥珀色 |
| "需要密钥" pill 在无项目时整行隐藏 | `MainWindow.axaml` (page header Grid 加 `IsVisible="{Binding HasProject}"`) | 视觉一致性 |
| Status bar 隔离模式小盾图标 | `MainWindow.axaml` + `AppStatusViewModel.IsIsolatedMode` | 视觉提示 AICHAT_ISOLATED_DATA_ROOT 状态 |
| Env panel "(Wave 7 拆独立 inspector)" 长句 → "(Wave 7)" 短句 | `EnvironmentPanelView.axaml` | 紧凑 |
| AppSettings + Clone 加 `DefaultAccess` / `FullAccessEnabled` / `EnvironmentPanelOpen` 3 字段 | `AppSettings.cs` + `ProtectedSettingsSerializer.cs` | schema 落定 |
| `MainWindow` ctor 注入 `IToastService` | `MainWindow.axaml.cs` | 3 个新 handler 弹 toast |

## 1. 范围（4 块）

### 1.1 3 栏主壳重构（plan §7 Wave 2 partial）
- 左 264px sidebar（保留）
- 中 flex chat 区
- 右 320px Environment 面板（**新建**，可折叠）
- 顶部栏保持
- Composer 仍然固定底部
- 启动自动 focus Composer（plan §7 Wave 2 退出条件）
- 折叠状态持久化到 AppSettings

### 1.2 Environment 面板 scaffold
4 个**真实数据** section（**不是 placeholder**）：
- **变更**：从现有 `WorkspaceChangeService.GetChangesAsync` 读，stat 数字
- **本地**：从现有 git data 读 current branch，静态显示
- **子智能体**：从现有 `AgentHost.SubAgentRuns` collection 计数
- **来源**：从现有 `PendingAttachments` collection 显示

未实现 section（明确写"暂未开放"）：
- **后台进程**：plan §7 Wave 7 第一个 PR 才做 supervisor，留空区 + "Wave 7" 标签
- **PR / Worktree / Diff 5 视图**：留空 + Wave 6 标签

### 1.3 Sidebar 顶部 5 个 first-level 入口图标
- **新对话**：active，绑 `NewConversationCommand`
- **拉取请求 / 站点 / 已安排 / 插件**：**4 个 disabled**，带 "Wave X" 角标，**不绑命令**（plan §5.4 明确禁止无功能入口）
  - 拉取请求 → "Wave 6"
  - 站点 → "Wave 9"
  - 已安排 → "Wave 9"
  - 插件 → "Wave 8"
- 视觉上要"5 个图标都在"，但 4 个用灰色 + 角标让用户知道未来会有

### 1.4 Composer 权限 Badge（2-toggle 模型）
- 跟 Codex 对齐（[2026-08-01-codex-settings-general.png](competitor-evidence/screenshots/2026-08-01-codex-settings-general.png)）
- 当前 NoWriteMode 升级：
  - `DefaultAccess` (default true) — 替代 NoWriteMode
  - `FullAccessEnabled` (default false) — 新增
- Composer badge 显示规则：
  - 两个都默认 → "默认访问"（绿/中性）
  - 只开 FullAccess → "完全访问"（红/警告）
  - 都关 → "只读"（中性）
- Badge 可点击，弹出 2 toggle 切换菜单（不打开 Settings modal）
- 跟现有 `ToolApprovalView` 配合：FullAccess + Ask-for-approval 默认 → 仍然弹审批

## 2. 决策（4 偏差方向）

| # | plan §13.5 偏差 | 我的决策 | 理由 | 后悔点 |
|---|---|---|---|---|
| 1 | Permissions 是 2 toggle 还是 3 档 | **2 toggle（Codex 风格）** | 截图直接证实 Codex 这么做；3 档是 plan 写错 | 跟现有 NoWriteMode 语义要重新映射（NoWrite = !DefaultAccess） |
| 2 | Plugin 6 类 | **本 Sprint 不动** | 涉及的是 Wave 8 Plugin 页面，不是主壳 | — |
| 3 | Subagent Failed 分组 | **本 Sprint 只显示计数** | 现有 SubAgentRuns 6 状态枚举已有 Failed；Environment 暂时只 show total | Wave 7 拆 |
| 4 | 项目 / chat 严格分离 vs 混排 | **保持当前严格分离** | 主壳 Sprint 0.5 不改项目模型，Wave 1+ 决定 | 改动小，未来反转成本低 |
| 5 | Sites 本地预览 / Run now / Plugin upgrade | **不涉及本 Sprint** | 全在 Wave 8/9 | — |

## 3. 不做（明确 scope 边界）

- ❌ 改动 `ProjectWorkspace` schema（Wave 1）
- ❌ 改 `Conversation` 持久化（Wave 1）
- ❌ 5 入口的 4 个 disabled 之外的逻辑（Wave 6/8/9）
- ❌ 插件 / Sites / Scheduled / PR 的真功能（Wave 6/8/9）
- ❌ BackgroundProcessSupervisor（Wave 7 第一个 PR）
- ❌ Sources 统一模型（Wave 7）
- ❌ 文件 chip（Wave 6 范围，本 Sprint 砍掉）
- ❌ 主壳之外的子页（Git modal / Settings modal 保持不变）

## 4. 验收标准

### 4.1 必须满足
- `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` 0 警告 0 错误
- `dotnet test` ≥ 733/733（基线持平，可能 +1 ~ +5 新测试）
- `git diff --check` 干净
- 隔离模式（`AICHAT_ISOLATED_DATA_ROOT=...`）启动正常
- 关闭 / 重新打开 Environment 面板 → 折叠状态保持
- 关 app → 重开 → sidebar 状态恢复（已有功能，不要破）
- 5 入口 disabled 不绑任何命令（plan §5.4 硬约束）
- 4 块新代码**全部走 `[RelayCommand]`** + 测试覆盖

### 4.2 视觉验收（screencapture）
- 启动后左 264 sidebar + 中 chat + 右 320 Environment 面板同时可见
- Environment 面板"变更"section 显示 `+0 -0`（空 repo）
- Environment 面板"子智能体"section 显示 `0 完成`（初始）
- Environment 面板"来源"section 显示 `暂无`
- Composer 权限 badge 显示"默认访问"（首次启动）
- Sidebar 5 个图标 1 active + 4 disabled 灰
- 当前 ~70 modified 文件**一行没动**（Sprint 0.5 是 additive）

## 5. 风险 + 控制

| 风险 | 控制 |
|---|---|
| MainWindow.axaml 改 3 列破坏现有 XAML bindings | 改前 grep 所有 `<MainWindow>` use-site；改后跑 733 个测试 |
| 5 入口 4 disabled 被误读为"5 个都该能用" | 角标 + 灰显 + Tooltip "Wave X 暂未开放" 3 重标识 |
| AppSettings 加字段漏 clone / normalize / 读取 | AGENTS.md 2026-07-31 4 步走法（field + clone + normalize + 构造点读）严格执行 |
| `MainWindowViewModel` 仍是 god object 候选 | Sprint 0.5 新建 `EnvironmentPanelViewModel` 独立类，**不**塞进 MainWindowViewModel |
| 用户没说要 2-toggle 偏好而我硬上 | Plan §1.4 写明决策 + 后悔点；用户跑完不喜欢，1 行 toggle 改回 |

## 6. 文件变更清单（实际）

| 文件 | 变更 | 大小 |
|---|---|---|
| `src/AIChat.App.Avalonia/Views/MainWindow.axaml` | 3 列重构 + 5 入口 + 权限 badge | ~+80 / -30 |
| `src/AIChat.App.Avalonia/Views/MainWindow.axaml.cs` | 默认 focus Composer + ⌘⇧E 快捷键 + 3 个 toast handler | ~+25 |
| `src/AIChat.App.Avalonia/ViewModels/EnvironmentPanelViewModel.cs` | **新建** 4 section + Attach/Detach lifecycle | ~165 |
| `src/AIChat.App.Avalonia/Views/Controls/EnvironmentPanelView.axaml(.cs)` | **新建** 独立 UserControl | ~+200 |
| `src/AIChat.App.Avalonia/ViewModels/MainWindowViewModel.cs` | 内联 permission badge（`DefaultAccess` / `FullAccessEnabled` / `PermissionBadgeText` / `CyclePermissionStateCommand` + 3 个 sidebar icon toast 命令 + `ToggleEnvironmentPanelCommand`） | ~+90 |
| `src/AIChat.Abstractions/Configuration/AppSettings.cs` | 加 3 字段：`DefaultAccess` / `FullAccessEnabled` / `EnvironmentPanelOpen` | ~+5 |
| `src/AIChat.Storage.Json/ProtectedSettingsSerializer.cs` | Clone 加 3 行 | +3 |
| `src/AIChat.App.Avalonia/App.axaml` | 加 4 样式（`Button.sidebar-brand` / `Button.sidebar-icon-row` / `Button.sidebar-icon-button` / `Button.chrome-button.permission-full` / `Border.model-chip`） | ~+30 |
| `src/AIChat.App.Avalonia/ViewModels/AppStatusViewModel.cs` | 加 `IsIsolatedMode` 派生属性 | ~+3 |
| `tests/AIChat.Tests/Avalonia/EnvironmentPanelViewModelTests.cs` | **新建** 10 tests（attach/detach/refresh/sub-agent/branch-prefix 剥离/git 错误） | ~265 |
| `tests/AIChat.Tests/Avalonia/MainWindowPermissionBadgeTests.cs` | **新建** 7 tests（三态显示 / cycle / 持久化 / NoWriteMode 联动） | ~165 |
| **合计** | — | ~+930 行（代码 + 测试） |

> 注：原 plan §6 估的是 "ComposerPermissionBadgeViewModel.cs 独立 VM ~80 行" — 实际落地时把这块逻辑内联进了 `MainWindowViewModel`（badge 只有 2 个 bool 派生 + 1 个 cycle 命令，提取独立类反而增加阅读跳转）。测试改用 `MainWindowPermissionBadgeTests` 通过完整 DI 容器（`AppHost.Build` + `InMemoryAppRepository`）走通 host VM 的实际写入路径。

## 7. 完成后报告

### 7.1 Build / test / diff
- `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` → 0 警告 0 错误
- `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` → **750 通过 / 750 总数**（基线 733 + 17 新增：10 EnvironmentPanel + 7 PermissionBadge）
- `git diff --check` → 干净

### 7.2 隔离模式启动验证
- 启动参数：`AICHAT_ISOLATED_DATA_ROOT=$(mktemp -d) dotnet run --project src/AIChat.App.Avalonia/...`
- 状态栏确认 "已加载（隔离会话：不读取系统钥匙串）"
- 截图：`docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-plus.png`（最终版，含 0.5+ 视觉 polish）
- 视觉确认：
  - ✅ 3 栏布局（左 264 sidebar / 中 chat / 右 320 Environment 面板）
  - ✅ Sidebar 顶部 "AIChat ▽" 模式切换器 + 🔍 搜索 + 🔔 通知 3 件组
  - ✅ 5 个 first-level 入口（1 active 绿 + 4 disabled 灰 + Wave X 角标）
  - ✅ Environment 面板 4 个 section（变更 / 本地 / 子智能体 / 来源）全部有"未实现"占位
  - ✅ Composer 权限 badge "默认" 显示（琥珀色 + 圆角，default state）
  - ✅ Titlebar 4 chrome 按钮（主题 / 设置 / 环境切换 / 帮助）+ 3 窗口控件
  - ✅ 启动自动 focus Composer（无 modal 弹出）

### 7.3 4 决策落地情况
| # | 决策 | 落地情况 |
|---|---|---|
| 1 | 2-toggle 权限 | ✅ AppSettings.DefaultAccess / FullAccessEnabled 字段 + Clone + 2 个独立 ObservableProperty + badge 显示 3 状态（默认/完全/只读）+ cycle 命令 |
| 2 | Plugin 6 类 | ⏸️ Sprint 0.5 不涉及（计划 Wave 8） |
| 3 | Subagent Failed | ⏸️ Sprint 0.5 只显示总计数（"0 个"），Wave 7 拆 |
| 4 | 项目 / chat 严格分离 | ✅ 保持现状（不动） |
| 5 | Sites / Run now / Plugin upgrade | ⏸️ 不涉及 Sprint 0.5 |

### 7.4 留给 Wave 1+ 的 hook
- `EnvironmentPanelOpen` 已经持久化到 AppSettings，schema ready
- `DefaultAccess` / `FullAccessEnabled` 已经 schema + persist，Codex 2-toggle 模型直接走
- 5 first-level 入口的 4 个 disabled 已经在 XAML 占位 + tooltip，Wave 6/8/9 只需 `IsEnabled="True"` + bind command
- `EnvironmentPanelView.axaml` 是独立 UserControl，Wave 5 加新 section 不影响 MainWindow
- 启动自动 focus Composer 走 `OnOpened` + `FocusPromptInput()` 复用
- ⌘⇧E 快捷键已绑 `ToggleEnvironmentPanelCommand`，与 ⌘⇧T/⌘⇧V/⌘⇧R/⌘⇧M 风格一致

### 7.5 新 untracked 文件
- `src/AIChat.App.Avalonia/ViewModels/EnvironmentPanelViewModel.cs`
- `src/AIChat.App.Avalonia/Views/Controls/EnvironmentPanelView.axaml`
- `src/AIChat.App.Avalonia/Views/Controls/EnvironmentPanelView.axaml.cs`
- `tests/AIChat.Tests/Avalonia/EnvironmentPanelViewModelTests.cs` (10 tests)
- `tests/AIChat.Tests/Avalonia/MainWindowPermissionBadgeTests.cs` (7 tests)
- `docs/SPRINT_0.5_PLAN.md` (本文件)
- `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-aichat.png`
- `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-post-fix.png` (post-fix screenshot)
- `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-plus.png` (Sprint 0.5+ final)
- `docs/competitor-evidence/screenshots/2026-08-01-handoff.png` (handoff snapshot)

### 7.6 修改的 M 文件
- `src/AIChat.Abstractions/Configuration/AppSettings.cs` (+3 字段)
- `src/AIChat.Storage.Json/ProtectedSettingsSerializer.cs` (+3 Clone 行)
- `src/AIChat.App.Avalonia/App.axaml` (+多组新样式)
- `src/AIChat.App.Avalonia/ViewModels/AppStatusViewModel.cs` (+IsIsolatedMode)
- `src/AIChat.App.Avalonia/ViewModels/MainWindowViewModel.cs` (+新 ViewModel property + commands + 3 partial handlers + 4 toast stubs)
- `src/AIChat.App.Avalonia/Views/MainWindow.axaml` (3-col + 5 nav icons + permission badge + env panel + titlebar 按钮 + sidebar 顶 3 件组)
- `src/AIChat.App.Avalonia/Views/MainWindow.axaml.cs` (⌘⇧E 快捷键 + OnOpened auto-focus + 3 toast handler)
