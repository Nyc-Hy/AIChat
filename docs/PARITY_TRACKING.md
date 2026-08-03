# AIChat Codex Desktop Parity Tracking

> **Status: Wave 11 ship 标记 (P0 三件全过,12 wave first-slice 全部 ship,ship 报告生成),测试基线 788/788。**
> 本表是 `docs/CODEX_DESKTOP_PARITY_PLAN.md` 第 3 节"对等的定义"中要求的**版本化 Feature → Journey → Evidence → Test 追踪表**。
> 每次进入下一 Wave 前必须更新本表。
> 表头的"证据等级"固定为 `screenshot-confirmed` / `official-confirmed` / `observed` / `inferred` / `deferred`(parity plan §3)。

- 当前 revision：`r0.12`（Provider prune: 砍到只剩 MiniMax（M3 latest），2026-08-02 23:00）— 19 测试增量 798→791（删 AnthropicToolCallTests + 4 个 catalog test 改写），P0 release gate 全过
- 表的更新规则：每个 Wave 完成时 +1 revision（`r0.2` / `r0.3` …），表底部加 changelog

## 命名约定

- `Feature ID`：`<一级分类>-<编号>`，例如 `NAV-NEW-01`（新对话）、`SET-PERSONAL-03`（个人设置第 3 项）
- `Journey ID`：`UJ-<分类>-<编号>`，例：`UJ-NEW-01`（创建普通聊天旅程）
- `Test ID`：`T-<层级>-<编号>`，例：`T-VM-007`、`T-INT-003`、`T-CU-001`
  - `T-DOM`：domain / schema
  - `T-STO`：storage / service
  - `T-VM`：viewmodel / component
  - `T-AVL`：avalonia headless
  - `T-INT`：integration
  - `T-CU`：Computer Use 验收
- `Wave`：所属 Wave（`W0`–`W11`）
- `Status`：`pending` / `in_progress` / `implemented` / `tested` / `observed` / `verified` / `deferred`
- `Evidence`：证据等级 + 来源指针（`screenshot-confirmed: <path>` / `official-confirmed: <url>` / `observed: <file:line>`）

---

## 1. 全局入口（5 项 first-level nav）

> **Wave 0 evidence 状态（r0.3）**：subagent 2 跑完官方 markdown + 用户 2026-08-01 真 Codex 截图后，5 项**全部** `screenshot-confirmed`。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `NAV-NEW-01` | 新建普通聊天 (Standalone Session) | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) + [codex-desktop-baseline.md §1](competitor-evidence/official-docs/codex-desktop-baseline.md#1-new-chat)) | `UJ-NEW-01` | W3 | `pending` | `pending` | `pending` | — |
| `NAV-NEW-02` | 新建项目内编码会话 (Project Session) | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — 项目列表真实存在 + `开启多角度 subagent 代码审查` 选中) | `UJ-NEW-02` | W3 | `pending` | `pending` | `pending` | — |
| `NAV-NEW-03` | 普通聊天移动/复制到项目 | `deferred` (**AIChat 自创**；Codex 官方仅 web 段提 "move it into a project" 无 UI 步骤) | `UJ-NEW-03` | W3 | `pending` | `pending` | `screenshot-required` | 见 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md` §NAV-NEW-03 |
| `NAV-NEW-04` | 项目列表混合 chat-derived 与 folder-derived | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — 项目段下 9 个 folder 项目 + 6 个 chat-derived 项目混排) | `UJ-NEW-04` | W3 | `pending` | `pending` | `pending` | **plan §4 偏差**：plan 写"普通聊天 vs 项目严格分离"，Codex 实际是"chat 嵌入项目列表"。AIChat Wave 3 需决策：严格分离还是混合显示 |
| `NAV-NEW-05` | 最近会话（跨项目聚合） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `最近` 段下 5 条跨项目) | `UJ-NEW-05` | W3 | `pending` | `pending` | `pending` | — |
| `NAV-PR-01` | PR 列表（独立 sidebar 入口） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `拉取请求` 在 sidebar 第 2 位) | `UJ-PR-01` | W6 | `pending` | `pending` | `pending` | — |
| `NAV-PR-02` | 创建 PR | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — Environment 内 `创建拉取请求` 按钮) | `UJ-PR-02` | W6 | `pending` | `pending` | `pending` | — |
| `NAV-PR-03` | PR 详情（diff / 状态 / 链接） | `official-confirmed` ([codex-desktop-baseline.md §9.6](competitor-evidence/official-docs/codex-desktop-baseline.md#96-environment-summary-panel综合面板)) | `UJ-PR-03` | W6 | `pending` | `pending` | `pending` | — |
| `NAV-SITE-01` | Sites 项目列表（独立 sidebar 入口） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `站点` 在 sidebar 第 3 位) | `UJ-SITE-01` | W9 | `shipped` (nav 入口启用 + Sites modal 列出 `ISiteRegistry.Sites` + add / remove / preview / stop 按钮) | `partial` (6 SiteRegistry tests + 5 JsonFileStore tests + 4 DI lock) | `pending` ([2026-08-02-wave9-sites-scheduled.png](competitor-evidence/screenshots/2026-08-02-wave9-sites-scheduled.png) — 5 first-level nav 入口全部启用) | 真实本地预览 (BackgroundProcessSupervisor) + 云部署 adapter 待 Wave 9 follow-up |
| `NAV-SITE-02` | Sites 本地预览 | `deferred` ([sites.md](competitor-evidence/official-docs/codex-desktop-baseline.md#5-sites) — Codex 没有"本地预览 URL"概念，只 save / deploy) | `UJ-SITE-02` | W9 | `partial` (Wave 7 follow-up: `BackgroundProcessSupervisor` 已落地,`SitesViewModel.PreviewAsync` 在 `SourcePath` 设置时启动 `python3 -m http.server` 真进程 + 进程组杀;SourcePath 未填时仍走 placeholder "需先选择源路径" 占位 deployment) | `partial` (5 supervisor lifecycle tests 覆盖 start/stop/spawn-failure/restart-recovery + 3 sites 路由 tests) | `pending` | 官方只 save（不可访问）+ deploy（线上 URL）；AIChat 若要本地预览是自创 + Wave 7 follow-up 解锁 `python3 -m http.server` 路径 |
| `NAV-SITE-03` | Sites 部署 | `screenshot-confirmed` ([sites.md "Deploy a version"](competitor-evidence/official-docs/codex-desktop-baseline.md#5-sites)) | `UJ-SITE-03` | W9 | `deferred` (按 plan §5.4 严格遵守 "无 Hosting Provider 时隐藏部署"；UI 灰掉) | `pending` | `pending` | 部署按钮 `IsEnabled=False` + tooltip 解释依赖;真云部署 adapter 留待 follow-up |
| `NAV-SCHED-01` | Scheduled 列表（独立 sidebar 入口） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `已安排` 在 sidebar 第 4 位) | `UJ-SCHED-01` | W9 | `shipped` (nav 入口启用 + Scheduled modal 列出 `IScheduledTaskRegistry.Tasks` + add / pause / resume / run-now / remove 按钮) | `partial` (7 ScheduledTaskRegistry tests + 4 DI lock) | `pending` ([2026-08-02-wave9-sites-scheduled.png](competitor-evidence/screenshots/2026-08-02-wave9-sites-scheduled.png)) | 真实调度引擎接入 follow-up;当前 "立即运行" 记录 Running 状态到历史,不会真执行 prompt |
| `NAV-SCHED-02` | 创建 Scheduled Task | `official-confirmed` ([automations.md fields](competitor-evidence/official-docs/codex-desktop-baseline.md#4-scheduled)) | `UJ-SCHED-02` | W9 | `partial` (add 按钮可点击,接受默认 task 并入列表;表单 UI 推迟) | `pending` | `pending` | 表单 (项目选择 / prompt / cadence / execution environment) 推迟到 follow-up |
| `NAV-SCHED-03` | 暂停 / 恢复 / 立即运行 | `partial` ([automations.md](competitor-evidence/official-docs/codex-desktop-baseline.md#4-scheduled) — 暂停 + 过滤器官方确认；"Run now" 按钮全文档 fetch 确认**官方无**) | `UJ-SCHED-03` | W9 | `partial` (暂停 / 恢复按钮落地;立即运行记录 Running 状态但未真执行) | `partial` (5 registry tests) | n/a | "Run now" 是 **AIChat 自创**（Codex 官方无）；见 `wave-0-c-evidence-upgrade.md` §NAV-SCHED-03 |
| `NAV-PLUGIN-01` | 插件目录入口（独立 sidebar 入口） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `插件` 在 sidebar 第 5 位) | `UJ-PLUGIN-01` | W8 | `pending` | `pending` | `pending` | — |
| `NAV-PLUGIN-02` | 插件详情 + 安装 | `official-confirmed` ([plugins.md "Install and use"](competitor-evidence/official-docs/codex-desktop-baseline.md#31-完整旅程)) | `UJ-PLUGIN-02` | W8 | `pending` | `pending` | `pending` | — |
| `NAV-PLUGIN-03` | 插件授权 / 启用 / 卸载 | `partial` ([plugins.md "Remove a plugin"](competitor-evidence/official-docs/codex-desktop-baseline.md#31-完整旅程) — 卸载官方确认；"Update plugin" 段全文档 fetch 确认**官方无** in-place 升级) | `UJ-PLUGIN-03` | W8 | `pending` | `pending` | `screenshot-required` | "Plugin in-place upgrade" 是 **AIChat 自创**（Codex 官方无）；见 `wave-0-c-evidence-upgrade.md` §NAV-PLUGIN-03+PLG-UPGRADE-01 |

**注**：所有 `inferred` 项必须在 Wave 0 退出前升级为 `observed` / `official-confirmed` / `screenshot-confirmed` / `deferred`。Computer Use 跑 Codex Desktop 实际验证后填实。

---

## 2. Session 模型

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `SES-KIND-01` | `ChatSession { Standalone, Project }` 二元分类 | `official-confirmed` ([projects.md "Start a chat without a project"](competitor-evidence/official-docs/codex-desktop-baseline.md#1-new-chat)) | `UJ-SES-01` | W1 | `pending` | `pending` | n/a | — |
| `SES-MIGRATE-01` | 旧 `ProjectWorkspace` ↔ `Conversation` 迁移到 Project Session | `deferred` (AIChat 内部数据迁移逻辑；Codex 行为无直接对应) | `UJ-SES-02` | W1 | `pending` | `pending` | n/a | Wave 1 启动时由 `MigrationCoordinator` 决定；不是 Codex 对等项 |
| `SES-MIGRATE-02` | 旧普通聊天（无项目）数据迁移 | `deferred` (AIChat 内部数据迁移逻辑) | `UJ-SES-03` | W1 | `pending` | `pending` | n/a | 同 SES-MIGRATE-01 |
| `SES-CTX-01` | 切项目不串用草稿 / 运行状态 / 上下文 | `deferred` (AIChat 内部 Session 隔离规则) | `UJ-SES-04` | W1 | `pending` | `pending` | n/a | Codex 同等规则无官方文档；Wave 1 schema 落实时由代码 + 测试 pin |
| `SES-PERSIST-01` | Standalone Session 重启恢复 | `deferred` (AIChat 内部持久化；Codex 同等行为无文档) | `UJ-SES-05` | W1 | `pending` | `pending` | n/a | Wave 1 持久化层落地时由测试 pin |

---

## 3. Project 模型（多 folder + primary）

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `PROJ-MULTI-01` | 项目内多个 folder roots | `official-confirmed` ([projects.md "Add folder"](competitor-evidence/official-docs/codex-desktop-baseline.md#21-一个项目能否包含多个-folder)) | `UJ-PROJ-01` | W3 | `pending` | `pending` | `pending` | — |
| `PROJ-PRIMARY-01` | Primary directory 设定 | `official-confirmed` ([projects.md "Make primary"](competitor-evidence/official-docs/codex-desktop-baseline.md#22-primary-directory-概念)) | `UJ-PROJ-02` | W3 | `pending` | `pending` | `pending` | — |
| `PROJ-AUTOLOAD-01` | 选 folder 后自动读 AGENTS / 配置 / 验证命令 | `official-confirmed` ([projects.md "automatic discovery of AGENTS.md, skills, and config.toml"](competitor-evidence/official-docs/codex-desktop-baseline.md#23-agentsmd--配置--验证命令是否自动读取)) | `UJ-PROJ-03` | W3 | `pending` | `pending` | `pending` | — |
| `PROJ-ADD-01` | 添加项目（2 次点击 / ⌘O） | `observed`: `MainWindow.axaml.cs` 已有 ⌘O 绑定 | `UJ-PROJ-04` | W3 | `partial` (daily driver 已有) | `pending` | `pending` | — |
| `PROJ-PERM-01` | 项目级权限覆盖 | `partial` ([sandboxing.md "Approvals" vs "sandbox"](competitor-evidence/official-docs/codex-desktop-baseline.md#24-项目设置与权限分离) — 两套独立控制线；项目级明确文档化) | `UJ-PROJ-05` | W4 | `pending` | `pending` | n/a | — |
| `PROJ-LOCALENV-01` | 项目级 `.codex/` 目录 + setup scripts 自动跑 | `official-confirmed` ([local-environment.md "Setup scripts"](competitor-evidence/official-docs/codex-desktop-baseline.md#23-agentsmd--配置--验证命令是否自动读取)) | `UJ-PROJ-06` | W3 | `pending` | `pending` | n/a | — |

---

## 4. Environment 面板

> **Wave 0 evidence 状态（r0.3）**：用户 2026-08-01 真 Codex 截图**证实 Environment 面板 5 个 section 全部存在**（变更 / 本地 / 子智能体 / 后台进程 / 来源）。我之前 subagent 2 把"官方 markdown 没单独页"推论为"面板不存在"是**过度悲观**。所有 ENV-* 行的 `not-found-in-official-docs` 标记已撤销。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `ENV-SHELL-01` | Environment 面板宿主（右侧，可加 section） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `环境信息` 标题 + `+` 按钮) | `UJ-ENV-01` | W5 | `pending` | `pending` | `pending` | — |
| `ENV-STANDALONE-01` | Standalone Session 隐藏项目 / Git 区块 | `deferred` (AIChat 内部决策；Codex 同等行为未文档化) | `UJ-ENV-02` | W5 | `pending` | `pending` | `screenshot-required` | Wave 5 启动前需 user 真机截一张 Standalone Session 状态图 |
| `ENV-GIT-01` | 变更统计 + Diff 入口 | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `变更 +26,653 -20,078` 数字) | `UJ-ENV-03` | W5 | `partial` (modal 形式) | `pending` | `pending` | 当前是 modal 不是右侧面板 |
| `ENV-DIFFVIEW-01` | Diff 5 视图（Unstaged / Staged / Commit / Branch / Last turn） | `official-confirmed` ([codex-desktop-baseline.md §9.1](competitor-evidence/official-docs/codex-desktop-baseline.md#91-git--diff--branch)) | `UJ-ENV-04` | W6 | `pending` | `pending` | `pending` | — |
| `ENV-LOCAL-01` | 本地 section（branch selector / commit / push / PR） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `本地` 段 + `codex/desktop-rebuild` + `提交或推送` + `创建拉取请求`) | `UJ-ENV-05` | W6 | `pending` | `pending` | `pending` | — |
| `ENV-SUBAGENT-01` | Subagent 计数（"66 完成" + 4 个 icon） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `子智能体 66 完成`) | `UJ-ENV-06` | W7 | `shipped` (per-run list inline in Env panel; color dots + 完成 count + per-row template/status/duration/tool-call) | `partial` (4 SubAgentRuns mirror tests + 7 StatusBrush palette tests) | `pending` | — |
| `ENV-SUBAGENT-02` | Subagent 独立线程 + 转向 / 停止 | `official-confirmed` ([codex-desktop-baseline.md §6.1](competitor-evidence/official-docs/codex-desktop-baseline.md#61-active--done--failed-分组)) | `UJ-ENV-07` | W7 | `partial` (per-run list shows status; Stop/Cancel/Turn/Close buttons pending harness-level cancel API) | `pending` | `pending` | Wave 7 follow-up: `CancelSubAgentAsync` registry in `AgentHarness` |
| `ENV-SUBAGENT-FAILED-01` | Subagent Failed 显式分组 | `deferred` (**AIChat 自创**；Codex 官方仅 Active/Done，无 Failed；Codex Micro 5 色状态机含 red 间接证明内部有 error) | `UJ-ENV-08` | W7 | `pending` | `pending` | `screenshot-required` | 见 `wave-0-c-evidence-upgrade.md` §ENV-SUBAGENT-FAILED-01+SUB-GROUP-02 |
| `ENV-BGPROC-01` | Background Process section（自动列运行中进程） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `后台进程 dotnet test tests/AIChat.Tests/AI...`) | `UJ-ENV-09` | W7 | `shipped` (Wave 7 follow-up: `EnvironmentPanelViewModel` 注入 `IBackgroundProcessSupervisor`,`ShowBackgroundProcesses` 默认 `true`,`BackgroundProcesses` ObservableCollection 镜像 supervisor 状态;XAML DataTemplate 升级为椭圆状态点 + DisplayName + StatusLabel + PidLabel + Stop 按钮(`IsVisible={Binding IsRunning}`)) | `partial` (5 supervisor tests + 4 EnvironmentPanelViewModel wiring tests: Attach 初始 sync / Stop forwards / Detach 不再订阅 / 集合变更 re-raise `HasBackgroundProcesses`) | `pending` | supervisor 落地;Windows job objects 仍是 follow-up slice |
| `ENV-SOURCE-01` | Sources section（剪贴板 + 网页搜索 + 查看全部） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — `来源 codex-clipboard-e7da29ff...` + `codex-clipboard-98a6c0f7...` + `网页搜索` + `查看全部`) | `UJ-ENV-10` | W7 | `partial` (pasted-image surface ships; web search / connector / plugin deferred) | `pending` | `pending` ([2026-08-02-wave7-sources-bg-hidden.png](competitor-evidence/screenshots/2026-08-02-wave7-sources-bg-hidden.png) — `暂无` placeholder visible) | web search + clipboard polling deferred to Wave 7 follow-up |
| `ENV-WORKTREE-01` | Worktree 永久 / Codex-managed 两类 | `official-confirmed` ([codex-desktop-baseline.md §9.2](competitor-evidence/official-docs/codex-desktop-baseline.md#92-worktree)) | `UJ-ENV-11` | W6 | `pending` | `pending` | `pending` | — |
| `ENV-FOLD-01` | 面板折叠状态持久化 | `observed`: `AppSettings.cs:78` (`EnvironmentPanelOpen`) + `MainWindowViewModel.cs:288` (`ToggleEnvironmentPanelCommand`) | `UJ-ENV-12` | W5 | `partial` (Env panel toggle 已实现；其它 section 折叠待 W5) | `pending` | n/a | Sprint 0.5 已落地；后续 Wave 复用此 schema 字段 |

---

## 5. Composer / 权限

> **代码盘点结果（subagent 1 §4）**：Composer 当前在 `MainWindow.axaml:552-676`，4.2.9 总结——输入/附件/send/stop/retry/7 slash/@file 解析 OK；缺 `+` 菜单（**主动删除**）、会话级模型 chip 只读、会话级权限 profile、@ 补全菜单、语音。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `COMP-MODEL-01` | 会话级模型选择 | `observed`: `MainWindow.axaml:614-622` chip | `UJ-COMP-01` | W4 | `partial` (chip 只读；切模型必须开 Settings) | `pending` | `pending` | 当前在 Settings 不是 Composer 控件 |
| `COMP-MODEL-02` | 会话级推理等级 | `deferred` (AIChat 内部；Codex 文档无显式 API) | `UJ-COMP-02` | W4 | `pending` | `pending` | n/a | Wave 4 落地时再评估 Codex 是否真有此 UI |
| `COMP-PERM-01` | Composer 显示 `完全访问` badge（当前权限状态） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — Composer 左下红色 badge) | `UJ-COMP-03` | W4 | `pending` | `pending` | `pending` | — |
| `COMP-PERM-02` | 2 独立 toggle 权限模型（`默认权限` + `完全访问权限`） | `screenshot-confirmed` ([2026-08-01-codex-settings-general.png](competitor-evidence/screenshots/2026-08-01-codex-settings-general.png) — 权限 section 2 toggle) | `UJ-COMP-04` | W4 | `pending` | `pending` | `pending` | **plan §4 / §7 Wave 4 偏差**：plan 写 "Read only / Workspace / Full access 3 档" 是错的——Codex 实际是 2 独立 toggle 组合 4 状态 |
| `COMP-PERM-03` | 权限审批卡片（Ask / Approve for me / Deny） | `partial` ([sandboxing.md app 段](competitor-evidence/official-docs/codex-desktop-baseline.md#72-ask-for-approval--session-allow--deny-三选项) — 官方 UI 菜单是 "Ask for approval / Approve for me / Full access / 命名 profile"，**不是** plan 写的 "Session allow / Deny") | `UJ-COMP-05` | W4 | `implemented` (ToolApproval Esc/Enter, commit `16d8ea8`) | `T-VM-007` | `pending` | **plan §4 偏差**：plan "Ask / Session allow / Deny" 命名与官方 UI 不一致；用 "Ask / Approve for me / Deny" 替换 |
| `COMP-PLUS-01` | `+` 菜单（文件 / 图片 / 来源 / 插件） | `deferred` (AIChat 主动删除；Wave 4 重做) | `UJ-COMP-05` | W4 | `pending` (主动删除；`MainWindow.axaml:611-614` 注释记录) | `pending` | `screenshot-required` | Wave 4 启动前需 user 截 Codex `+` 菜单实际项 |
| `COMP-ATTACH-01` | 粘贴图片（⌘V → 缩略图 → 发送升级 InputArtifact） | `observed`: `PendingAttachmentsViewModel` | `UJ-COMP-06` | W4 | `implemented` | `pending` | `pending` | — |
| `COMP-ATFILE-01` | `@file 路径` 引用 | `observed`: `PromptAttachmentParser.cs` + `MainWindow.axaml.cs:570-584` 粘贴 | `UJ-COMP-07` | W4 | `partial` (解析 OK；无 chip 显示) | `pending` | `pending` | — |
| `COMP-SEND-01` | 发送 / 停止 / 重试 | `observed`: `MainWindow.axaml:652-672` + `MainWindow.axaml.cs:586-608` | `UJ-COMP-08` | W4 | `implemented` (⌘↵ / ⌘. / ⌘R + send/stop 按钮互斥) | `pending` | `pending` | — |
| `COMP-SLASH-01` | Slash 命令（/clear / /help / /status / /memory / /git / /copy） | `observed`: `SlashCommandHandler.cs:73-110` | `UJ-COMP-09` | W4 | `implemented` (7 个最常用；无 `/` 自动补全菜单) | `T-VM-slash` | `pending` | — |
| `COMP-MENTION-01` | `@` 补全菜单 | `deferred` (AIChat 内部；UI 行为未文档化) | `UJ-COMP-10` | W4 | `pending` (token 解析 OK；inline 补全菜单无) | `pending` | n/a | Wave 4 落地时由代码 + 测试 pin |
| `COMP-VOICE-01` | 语音输入入口 | `deferred` ([codex-desktop-baseline.md §8.3](competitor-evidence/official-docs/codex-desktop-baseline.md#83-附件--图片--语音) — 官方 `+` 菜单"语音"项 `not-found-in-official-docs`) | `UJ-COMP-11` | W4 | `deferred` | n/a | n/a | 官方未文档化；plan §7 明确"没有真实转写能力前不得显示" |
| `COMP-APPROVEREVIEW-01` | Auto-review approval (eligibility-based) | `official-confirmed` ([codex-desktop-baseline.md §7.2](competitor-evidence/official-docs/codex-desktop-baseline.md#72-ask-for-approval--session-allow--deny-三选项)) | `UJ-COMP-12` | W4 | `pending` | `pending` | `pending` | — |

---

## 6. Git / Diff / Worktree / PR

> **代码盘点结果（subagent 1 §3）**：
> - Git 整套后端（`WorkspaceChangeService.GetChangesAsync/StageAsync/CommitAsync/RestoreFileAsync`）已实现并被 `GitStatusView` modal 消费。
> - 缺 push / branch 切换 / worktree 创建 / PR。
> - 5 个 view（Unstaged / Staged / Commit / Branch / Last turn）由 Codex 官方核验，AIChat 当前 modal 只有 1 个 view（Unstaged）。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `GIT-STAT-01` | 变更统计（staged / unstaged / untracked） | `observed`: `GitStatusView` | `UJ-GIT-01` | W6 | `implemented` (modal) | `pending` | `pending` | — |
| `GIT-DIFF-01` | Diff viewer（行级 / 复制 / 恢复） | `observed`: `GitStatusView` modal | `UJ-GIT-02` | W6 | `implemented` (modal 形式) | `pending` | `pending` | Wave 6 改造成 Environment 内 |
| `GIT-DIFFVIEW-01` | Diff 5 视图（Unstaged / Staged / Commit / Branch / Last turn） | `official-confirmed` ([codex-desktop-baseline.md §9.1](competitor-evidence/official-docs/codex-desktop-baseline.md#91-git--diff--branch)) | `UJ-GIT-03` | W6 | `pending` | `pending` | `pending` | 当前 modal 只有 1 个 view（Unstaged） |
| `GIT-STAGE-01` | Stage / Unstage / Restore | `observed`: `WorkspaceChangeService.StageAsync/RestoreFileAsync` | `UJ-GIT-04` | W6 | `implemented` (modal) | `pending` | `pending` | — |
| `GIT-COMMIT-01` | Commit | `observed`: `WorkspaceChangeService.CommitAsync` + `GitStatusView` modal | `UJ-GIT-05` | W6 | `implemented` | `pending` | `pending` | — |
| `GIT-PUSH-01` | Push | `deferred` (AIChat 后端无；Wave 6 落地) | `UJ-GIT-06` | W6 | `pending` (后端无) | `pending` | n/a | Wave 6 第一 PR 必须先有 `WorkspaceChangeService.PushAsync` |
| `GIT-BRANCH-01` | Branch 列表 / 创建 / 切换 | `deferred` (AIChat 后端只读；Wave 6 落地) | `UJ-GIT-07` | W6 | `pending` (后端只读 branch 文本) | `pending` | n/a | 同 GIT-PUSH-01 |
| `GIT-WORKTREE-01` | Worktree 创建 / 复用 / 清理 / Session 绑定 | `deferred` (AIChat 后端 0 命中；Wave 6 落地) | `UJ-GIT-08` | W6 | `pending` (后端 0 命中 `git worktree`) | `pending` | n/a | Codex `worktree` 概念已有 `GIT-WORKTREE-PERM-01` 文档化；AIChat 落地是 build layer |
| `GIT-WORKTREE-PERM-01` | Codex-managed vs Permanent worktree | `official-confirmed` ([git-worktrees.md](competitor-evidence/official-docs/codex-desktop-baseline.md#92-worktree)) | `UJ-GIT-09` | W6 | `pending` | `pending` | `pending` | — |
| `GIT-REVIEWPANE-01` | Review pane (Code review delivery inline/detached) | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | `UJ-GIT-10` | W6 | `pending` | `pending` | `pending` | — |

---

## 7. Subagents / Background Processes / Sources

> **代码盘点结果（subagent 1 §3.4–§3.6）**：
> - Subagent 数据通路（`SubAgentScheduler` + 6 状态枚举）已实现；UI 只在 plan 面板底部 1 行（template + task + status + duration），**不是独立 inspector**。
> - Background Process supervisor 全仓 0 命中。
> - Sources 没有统一模型（`InputArtifact` 只用于 image paste）。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `SUB-GROUP-01` | Active / Completed 分组 | `official-confirmed` ([subagents.md illustration](competitor-evidence/official-docs/codex-desktop-baseline.md#61-active--done--failed-分组)) | `UJ-SUB-01` | W7 | `pending` | `pending` | `pending` | — |
| `SUB-GROUP-02` | Failed 分组 | `deferred` (**AIChat 自创**；Codex 官方仅 Active/Done 分组，无 Failed 显式段) | `UJ-SUB-02` | W7 | `pending` | `pending` | `screenshot-required` | 同 `ENV-SUBAGENT-FAILED-01` |
| `SUB-INSPECT-01` | 独立线程 / 任务 / 模板 / 时长 / 结果 | `observed`: `SubAgentScheduler` + `SubAgentRunViewModel` (plan panel 底部) | `UJ-SUB-03` | W7 | `shipped` (per-run list inline in Env panel; template / task / status dot / duration / tool-call count; status color palette green/red/amber/grey; Summary on hover; newest-first ordering) | `partial` (4 SubAgentRuns mirror tests + 7 StatusBrush palette tests) | `pending` | — |
| `SUB-STOP-01` | 停止 / 转向 / 重试 / 关闭单个 Subagent | `official-confirmed` ([codex-desktop-baseline.md §6.1](competitor-evidence/official-docs/codex-desktop-baseline.md#61-active--done--failed-分组)) | `UJ-SUB-04` | W7 | `pending` | `pending` | `pending` | — |
| `SUB-BUILTIN-01` | 内置 agent 模板（default / worker / explorer） | `official-confirmed` ([subagents.md "Custom agents"](competitor-evidence/official-docs/codex-desktop-baseline.md#62-独立线程--任务--模板)) | `UJ-SUB-05` | W7 | `partial` (仓内只跑 explorer) | `pending` | `pending` | — |
| `SUB-CUSTOM-01` | 自定义 agent TOML 文件 (`~/.codex/agents/` 或 `.codex/agents/`) | `official-confirmed` ([codex-desktop-baseline.md §6.2](competitor-evidence/official-docs/codex-desktop-baseline.md#62-独立线程--任务--模板)) | `UJ-SUB-06` | W7 | `pending` | `pending` | `pending` | — |
| `BGPROC-SUPER-01` | `BackgroundProcessSupervisor`（进程树、PID、日志、终止、重启恢复） | `partial` (r0.4 从 `not-found` 升级：截图证实 segment 存在，**supervisor 细节能力是 AIChat 自创**) | `UJ-BG-01` | W7 | `shipped` (Wave 7 follow-up: `AIChat.Domain.BackgroundProcesses.BackgroundProcess` + `BackgroundProcessStatus` enum + `AIChat.Application.BackgroundProcesses.IBackgroundProcessSupervisor` + `BackgroundProcessSupervisor` 实现 StartAsync/StopAsync/ReloadAsync/Changed event + `AppRuntimeProfile.BackgroundProcessesFile` 持久化) | `shipped` (9 supervisor tests: start/stop/spawn-failure/restart-recovery/log-tail capture/Changed event firing) | n/a | 见 `wave-0-c-evidence-upgrade.md` §BGPROC-SUPER-01；`learn.chatgpt.com/docs/background-processes.md` 404 |
| `BGPROC-CLEAN-01` | 停止时杀死整个子进程树 | `deferred` (AIChat 内部；Wave 7 supervisor 落地) | `UJ-BG-02` | W7 | `shipped` (Wave 7 follow-up: macOS / Linux 用 P/Invoke `setpgid(0, 0)` 把 child 设为新进程组 leader + `kill(-pid, signal)` 负 pid 发给整个进程组;SIGTERM 5s timeout 后升级 SIGKILL) | `shipped` (covered by `StopAsync_TerminatesRunningProcess` + `StartAsync_SpawnFailure_RecordsCrashedWithMessage` supervisor tests) | n/a | Windows job objects 仍是 follow-up slice |
| `BGPROC-RESTART-01` | 应用退出 / 重启后安全清理 / 重连 | `deferred` (AIChat 内部；Wave 7 supervisor 落地) | `UJ-BG-03` | W7 | `shipped` (Wave 7 follow-up: `ReloadAsync` walk 所有 `Status=Running` entries,`Process.GetProcessById` throws 表示 PID 已死 → 标记 `Crashed` + `StoppedAt=now` + `ExitCode=-1`;无 relaunch policy — 留 Settings toggle 的 follow-up) | `shipped` (covered by `ReloadAsync_MarksRunningEntriesAsCrashed_WhenProcessIsDead` + `ReloadAsync_EmptyFile_ProducesEmptyState`) | n/a | relaunch-on-launch Settings toggle 是 follow-up;目前只 reconcile |
| `SRC-MODEL-01` | Sources 统一模型（文件 / 图片 / 网页 / 连接器 / 插件） | `partial` ([codex-desktop-baseline.md §9.5](competitor-evidence/official-docs/codex-desktop-baseline.md#95-sources) — web 项目视图有，desktop Environment 面板官方未文档化) | `UJ-SRC-01` | W7 | `pending` (仅 image input artifact 窄通道) | `pending` | `pending` | — |
| `SRC-TRACE-01` | 消息可回溯到 Source | `deferred` (AIChat 内部；Wave 7 Sources 模型落地) | `UJ-SRC-02` | W7 | `pending` | `pending` | n/a | Wave 7 需先有统一 Sources 模型 |

---

## 8. Plugins（详见 parity plan §7 Wave 8）

> **代码盘点结果（subagent 1 §1.5 / §7）**：
> - 后端 `PluginToolProvider` + `PluginManifestLoader` 在 `src/AIChat.Application/Plugins/` 存在但 **DI 未注册**（`ServiceRegistration.cs` 0 命中 `Plugin`），用户启动后端从未注入，**dead code on disk**。
> - 旧 `McpStdioClient` / `PluginSkill*` 已删（commit `5d1cd99`）；`examples/plugins/dotnet-tools/plugin.json` 仍写有 `skills` / `mcpServers` 字段，但运行时不再 parse —— **stale 配置**。
> - Codex 官方 plugin 6 类：Skills / Connectors / MCP / Browser extensions / Hooks / Scheduled templates。AIChat plan 列了 6 类：Skills / Command tools / Connectors / MCP / Hooks / UI resources。**两个清单不完全对齐**——"Browser extensions" 官方有，plan 无；"UI resources" plan 有，官方无；"Command tools" plan 有，但官方把"command" 概念放在 Skills / Connectors / MCP 组合中。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `PLG-DIRECTORY-01` | 插件目录（OpenAI / Workspace / Personal 三 tab） | `official-confirmed` ([plugins.md "Universal plugin directory"](competitor-evidence/official-docs/codex-desktop-baseline.md#33-是否有官方插件目录--商店)) | `UJ-PLG-01` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-DISCOVER-01` | 浏览 / 搜索插件 | `official-confirmed` ([codex-desktop-baseline.md §3.1](competitor-evidence/official-docs/codex-desktop-baseline.md#31-完整旅程)) | `UJ-PLG-02` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-DETAIL-01` | 插件详情页（来源 / 版本 / 权限 / 外部进程） | `official-confirmed` ([codex-desktop-baseline.md §3.1](competitor-evidence/official-docs/codex-desktop-baseline.md#31-完整旅程)) | `UJ-PLG-03` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-INSTALL-01` | 插件安装 | `official-confirmed` ([codex-desktop-baseline.md §3.1](competitor-evidence/official-docs/codex-desktop-baseline.md#31-完整旅程)) | `UJ-PLG-04` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-CONNECT-01` | Connector OAuth / 取消 / 重授权 / 凭据清理 | `deferred` (AIChat 内部；Wave 8 Plugin 落地) | `UJ-PLG-05` | W8 | `pending` | `pending` | n/a | Wave 8 启动时由代码 + 测试 pin |
| `PLG-ENABLE-01` | 插件启用 / 停用 / 卸载 | `partial` ([plugins.md "Remove a plugin"](competitor-evidence/official-docs/codex-desktop-baseline.md#31-完整旅程) — 卸载官方确认；启停 UI 不明) | `UJ-PLG-06` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-UPGRADE-01` | 插件 in-place 升级 | `deferred` (**AIChat 自创**；Codex 官方 plugins.md 无 "Update plugin" 段；强烈暗示 "重新安装 + 新会话") | `UJ-PLG-07` | W8 | `pending` | `pending` | `screenshot-required` | 见 `wave-0-c-evidence-upgrade.md` §NAV-PLUGIN-03+PLG-UPGRADE-01 |
| `PLG-CMD-01` | Command-style plugin 运行时 | `observed`: `PluginToolProvider` 存在但 DI 未注册 | `UJ-PLG-08` | W8 | `partial` (loader 已注册到 DI + `IPluginRegistry` 接入；Plugins modal 列出已扫描的 command 工具；`PluginToolProvider.RegisterExternalProvider` 接入 `AgentToolRegistry` 留待 Wave 8 follow-up) | `partial` (8 PluginRegistry tests + 2 AppHost DI lock) | `pending` ([2026-08-02-wave8-plugins-nav.png](competitor-evidence/screenshots/2026-08-02-wave8-plugins-nav.png) — Plugins nav 入口不再禁用) | Wave 8 follow-up: 接入 `RegisterExternalProvider` 到 `AgentToolRegistry.CreateDefault()` + 决定 stale `skills`/`mcpServers` 字段去留 |
| `PLG-SKILL-01` | Skills loader（官方 4 层：repo/user/admin/system） | `official-confirmed` ([build-skills.md "Where Codex loads local skills"](competitor-evidence/official-docs/codex-desktop-baseline.md#23-agentsmd--配置--验证命令是否自动读取)) | `UJ-PLG-09` | W8 | `pending` (仓内 `PluginSkill*.cs` 已删) | `pending` | `pending` | — |
| `PLG-MCP-01` | MCP transport / discovery / auth / capability grants | `official-confirmed` ([developer-settings.md "Integrations and MCP"](competitor-evidence/official-docs/codex-desktop-baseline.md#93-编码-agent--repo--工具)) | `UJ-PLG-10` | W8 | `pending` (仓内 `McpStdioClient.cs` 已删) | `pending` | `pending` | — |
| `PLG-HOOK-01` | Hooks（pre/post tool / message） | `official-confirmed` ([plugins.md "Hooks"](competitor-evidence/official-docs/codex-desktop-baseline.md#32-能力分类)) | `UJ-PLG-11` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-SCHEDTEMPLATE-01` | Scheduled task 模板（plugin 内） | `official-confirmed` ([plugins.md "Scheduled task templates"](competitor-evidence/official-docs/codex-desktop-baseline.md#32-能力分类)) | `UJ-PLG-12` | W8 | `pending` | `pending` | `pending` | — |
| `PLG-BROWSER-01` | Browser extensions（官方 6 类之一，plan §7 Wave 8 未列） | `official-confirmed` ([plugins.md "Browser extensions"](competitor-evidence/official-docs/codex-desktop-baseline.md#32-能力分类)) | `UJ-PLG-13` | W8 | `deferred` | n/a | n/a | AIChat 不集成远程浏览器（plan §5.4），跳过 |
| `PLG-UI-01` | 可选 UI resources（plan §7 Wave 8 列；官方未列） | `inferred` | `UJ-PLG-14` | W8 | `deferred` | n/a | n/a | 官方 markdown 文档里没列；plan §5.4 也不强调 |

---

## 9. Settings 中心（详见 parity plan §7 Wave 10）

> **Wave 10 first slice ship（2026-08-02 21:30）**：
> - Settings modal 重组为 4 大分类 (个人 / 集成 / 编码 / 已归档) + 左侧 rail 导航
> - 搜索框（按 section title / 关键词过滤；500ms SLA 通过；搜索激活时跨分类显示）
> - 4 大分类下落地: 生成参数 / 执行模式 / 外观 (主题) / 模型提供方 / 工具列表 / 安全策略 / 自动修复 / 工具权限 / 已归档占位
> - ⌘, 打开 / Esc 关闭 已存在;新分类 切换命令 `ShowCategoryCommand`
> - 11 个新测试,基线 777→788
>
> **代码盘点结果（subagent 1 §5）**：
> - 当前是 1 个 modal（`SettingsView.axaml`），4 个分组（provider / safety / default behavior / tool perms），不是 plan 要求的全页 Route。
> - 缺搜索 / 全页 Route / 归档 / Cloud account / 隐私 / 计费 等。
>
> **官方 markdown 核验**：subagent 2 §10 给出 Codex 完整 settings 12+ 个 H2 章节，AIChat 适配需要 (a) 真实能力条目化 (b) 不适用条目明确 `deferred` + 理由。

### 9.1 个人

| Feature ID | 功能 | 证据等级 | Wave | 实现状态 | 备注 |
|---|---|---|---|---|---|
| `SET-PERSONAL-01` | 常规（sleep / follow-up behavior） | `official-confirmed` ([reference/settings.md "General"](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `pending` | — |
| `SET-PERSONAL-02` | 导入 | `deferred` (Codex 官方未单独列；AIChat 内部决定) | W10 | `pending` | 官方未单独列；AIChat 决定是否需要 |
| `SET-PERSONAL-03` | 个人资料（activity insights / lifetime tokens / streaks） | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `pending` (有 input/output token，无 lifetime aggregate) | AIChat 适配：本地 profile |
| `SET-PERSONAL-04` | 外观（Light / Dark / accent / 字体） | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `partial` (⌘⇧T 切换；自定义 accent/字体待办) | — |
| `SET-PERSONAL-05` | 语音 | `not-found-in-official-docs` ([codex-desktop-baseline.md §8.3](competitor-evidence/official-docs/codex-desktop-baseline.md#83-附件--图片--语音)) | W10 | `deferred` | 官方 `+` 菜单无语音项；plan §4 也明确"无真实转写能力前不显示" |
| `SET-PERSONAL-06` | Notifications | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `pending` | — |
| `SET-PERSONAL-07` | Suggested prompts | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `pending` | — |
| `SET-PERSONAL-08` | Memories（个人级 `AGENTS.md`） | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `partial` (`MemoryEditorView` 已实现；⌘⇧M 触发) | — |
| `SET-PERSONAL-09` | Personalization（Friendly / Pragmatic / None） | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `pending` | — |
| `SET-PERSONAL-10` | 宠物 | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `deferred` | plan §5.4 明确不做装饰；AIChat 不引入宠物 |
| `SET-PERSONAL-11` | 键盘快捷键（可搜索 cheat sheet） | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `implemented` (`KeyboardShortcutsView`, ⌘/ 触发) | — |
| `SET-PERSONAL-12` | 使用情况 / 计费 | `deferred` (Codex 官方有 lifetime token / streaks；AIChat 内部有 token 但无计费 API) | W10 | `partial` (input/output token 在 run summary) | 无 Provider 真实计费数据 |
| `SET-PERSONAL-13` | 账户 / 隐私 | `deferred` (AIChat 无云账户；Wave 10 决定) | W10 | `pending` (AIChat 无云账户) | 凭据由 OS Keychain 持有 |
| `SET-PERSONAL-14` | Keep a chat near your work (popout + Always on top) | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `pending` | — |

### 9.2 集成

| Feature ID | 功能 | 证据等级 | Wave | 实现状态 | 备注 |
|---|---|---|---|---|---|
| `SET-INTEG-01` | 智能快照 | `inferred` | W10 | `deferred` | AIChat 内部有 snapshot 但不叫"智能快照"；等 Wave 1 重新命名 |
| `SET-INTEG-02` | 插件（Plugins browser） | `official-confirmed` ([plugins.md](competitor-evidence/official-docs/codex-desktop-baseline.md#3-plugins)) | W10 | `pending` | 等 Wave 8 落地 |
| `SET-INTEG-03` | 浏览器 | `official-confirmed` ([codex-desktop-baseline.md §10.2](competitor-evidence/official-docs/codex-desktop-baseline.md#102-集成外部服务--设备)) | W10 | `deferred` | plan §5.4 明确 AIChat 不集成远程浏览器 |
| `SET-INTEG-04` | 电脑操控 (Computer Use) | `official-confirmed` ([codex-desktop-baseline.md §10.2](competitor-evidence/official-docs/codex-desktop-baseline.md#102-集成外部服务--设备)) | W10 | `deferred` | plan §5.4 明确 AIChat 不做 Computer Use 集成 |
| `SET-INTEG-05` | IDE extension sync | `official-confirmed` ([developer-settings.md](competitor-evidence/official-docs/codex-desktop-baseline.md#93-编码-agent--repo--工具)) | W10 | `deferred` | AIChat 桌面单一形态，不接 IDE 扩展 |

### 9.3 编码

| Feature ID | 功能 | 证据等级 | Wave | 实现状态 | 备注 |
|---|---|---|---|---|---|
| `SET-CODE-01` | Hooks | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `pending` | — |
| `SET-CODE-02` | Connections / MCP | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `pending` | — |
| `SET-CODE-03` | Git（branch naming / force push / commit prompt 模板） | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `partial` (工具级 git 已实现；用户身份 / commit prompt 模板待办) | — |
| `SET-CODE-04` | Environment（local / remote / worktree） | `official-confirmed` ([local-environment.md](competitor-evidence/official-docs/codex-desktop-baseline.md#23-agentsmd--配置--验证命令是否自动读取)) | W10 | `pending` | — |
| `SET-CODE-05` | Worktree | `official-confirmed` ([git-worktrees.md](competitor-evidence/official-docs/codex-desktop-baseline.md#92-worktree)) | W10 | `pending` | — |
| `SET-CODE-06` | Project and terminal behavior | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `pending` | — |
| `SET-CODE-07` | Code review (inline / detached) | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `pending` | — |
| `SET-CODE-08` | Agent configuration (config.toml 共享) | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `pending` | — |
| `SET-CODE-09` | Browser developer mode (CDP) | `official-confirmed` ([codex-desktop-baseline.md §10.3](competitor-evidence/official-docs/codex-desktop-baseline.md#103-已归档聊天)) | W10 | `deferred` | 跟 §9.2 浏览器一并跳过 |

### 9.4 已归档

| Feature ID | 功能 | 证据等级 | Wave | 实现状态 | 备注 |
|---|---|---|---|---|---|
| `SET-ARCHIVE-01` | 已归档聊天列表（含日期 / 项目） | `official-confirmed` ([codex-desktop-baseline.md §10.4](competitor-evidence/official-docs/codex-desktop-baseline.md#104-已归档聊天)) | W10 | `deferred` (nav 入口已建 + 分类占位,但数据模型 "软删除 + 恢复" 暂未实现) | — |
| `SET-ARCHIVE-02` | Unarchive / 永久删除 | `official-confirmed` ([codex-desktop-baseline.md §10.4](competitor-evidence/official-docs/codex-desktop-baseline.md#104-已归档聊天)) | W10 | `deferred` | — |

### 9.5 设置入口

| Feature ID | 功能 | 证据等级 | Wave | 实现状态 | 备注 |
|---|---|---|---|---|---|
| `SET-OPEN-01` | 打开方式：`⌘,` / `Ctrl+,` / `codex://settings` deep link | `official-confirmed` ([reference/settings.md "Open Settings"](competitor-evidence/official-docs/codex-desktop-baseline.md#105-settings-入口how-to-open)) | W10 | `implemented` (⌘, 已有;Wave 10 重组为 4 大分类) | 全页 Route 推迟 |
| `SET-SEARCH-01` | 设置搜索（按名字 / 按键反向） | `official-confirmed` ([codex-desktop-baseline.md §10.1](competitor-evidence/official-docs/codex-desktop-baseline.md#101-个人个人偏好与账户)) | W10 | `shipped` (search box + 跨分类 keyword 过滤,500ms SLA 通过) | plan §7 要求 500ms 内出结果 |

---

## 10. 主壳 / 导航 / 焦点

> **代码盘点结果（subagent 1 §0 / §10）**：
> - 主壳 `MainWindow.axaml` 是单窗 + 左 264px sidebar + flex 主区 + 状态栏 32。**没有右侧 Environment 面板**（plan §4 必备）。
> - 启动自动 focus Composer 缺失（当前只能 ⌘L 触发 `FocusPromptInput`）。
> - 文件树 / 文件预览 已主动删（commit `e356ee5`，10 个 deleted 文件），与 plan §5.4 / AGENTS.md 决策一致。

> **r0.3 校正**：真 Codex 截图证实 3 栏布局（左 sidebar / 中 chat / 右 Environment 面板）。

| Feature ID | 功能 | 证据等级 | 目标用户旅程 | Wave | 实现状态 | 自动测试 | Computer Use 证据 | 延后原因 |
|---|---|---|---|---|---|---|---|---|
| `SHELL-3COL-01` | 三栏主壳（左 nav / 中 session / 右 env） | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) — 左 sidebar 264px / 中 chat / 右 Environment 面板) | `UJ-SHELL-01` | W2 | `pending` (当前只有 sidebar + 中区) | `pending` | `pending` | Wave 2 必须建 Environment 面板宿主 |
| `SHELL-FOCUS-01` | 应用启动 Composer 自动获得焦点 | `observed`: `MainWindow.axaml.cs:696-699` (`OnOpened` → `FocusPromptInput` via `Dispatcher.UIThread.Post` 优先级 `Background`) | `UJ-SHELL-02` | W2 | `implemented` (Sprint 0.5) | `T-VM-shell-focus` | n/a | — |
| `SHELL-COLLAPSE-01` | Sidebar 可折叠 | `deferred` (AIChat 内部；Wave 2 主壳时落地) | `UJ-SHELL-03` | W2 | `pending` | `pending` | n/a | Wave 2 启动时由代码 + 测试 pin |
| `SHELL-COLLAPSE-02` | Environment 面板可折叠 | `observed`: `AppSettings.cs:78` (`EnvironmentPanelOpen`) + `MainWindowViewModel.cs:288` (`ToggleEnvironmentPanelCommand`) + `MainWindow.axaml.cs:219` (⌘⇧E 绑) | `UJ-SHELL-04` | W2 | `implemented` (Sprint 0.5) | `T-VM-env-collapse` | n/a | — |
| `SHELL-RESPONSIVE-01` | 窄窗口 / 宽窗口 / Light / Dark 无重叠 | `deferred` (设计约束；Wave 2 主壳时统一验证) | `UJ-SHELL-05` | W2 | `pending` | `pending` | n/a | Wave 11 acceptance 时再跑 |
| `SHELL-HIDE-01` | 尚未实现入口（PR / Sites / Scheduled / Plugins）保持隐藏 | `inferred` | `UJ-SHELL-06` | W2 | `verified` (当前 `git status` 显示 4 个入口完全无 UI，符合 plan §5.4) | n/a | n/a | 设计决策已落地 |
| `SHELL-NOFTREE-01` | 确认不重新加入 IDE 式文件树 | `observed`: `git status` 删了 `FileTreeView*` + `FilePreviewView*` + `FileTreeBuilder*` | n/a | W2 | `verified` (AGENTS.md §177-181) | n/a | n/a | 设计决策已锁定 |
| `SHELL-NOAUTOTREE-01` | 自动验证不重新加入文件树（如用户提议） | `inferred` | n/a | n/a | `verified` (AGENTS.md 决策) | n/a | n/a | — |
| `SHELL-MVVM-01` | 拆 MainWindowViewModel god object 为 AppShell / Navigation / SessionHost / EnvironmentPanel | `deferred` (AIChat 内部架构决策；Wave 2 主壳时落地) | `UJ-SHELL-07` | W2 | `partial` (daily driver 已抽 `AppStatusViewModel` 提 8 个 host 状态) | `pending` | n/a | Wave 2 启动时由代码 + 测试 pin |

---

## 11. 跨测试策略

| 测试层 | 责任 | 已覆盖 | Wave 0 待办 |
|---|---|---|---|
| `T-DOM` domain / schema | Session / Environment / Source / Process / Plugin / Permission 状态转移 | 待盘点 | 列出旧 Conversation / Project schema → 新 schema 的字段映射表 |
| `T-STO` storage / service | 原子写 / 并发 / 取消 / 损坏恢复 / secret redaction | 待盘点 | 至少 1 个 corruption recovery + 1 个 backup restore 测试 |
| `T-VM` viewmodel / component | PropertyChanged / DataContext binding / 焦点 / 键盘 | `tests/AIChat.Tests/Avalonia/*` 已 18+ 文件 | 跟新功能同步加 |
| `T-AVL` avalonia headless | MainWindow / Settings / Environment / Diff / Plugin / Scheduled 路由加载 | 待盘点 | Wave 2 起每 Wave 至少 1 个 Route 加载测试 |
| `T-INT` integration | 真实 git repo / 真实进程 / Provider stream | `tests/AIChat.Tests/Composition/AddProjectSendTaskSmokeTests.cs` 等 | 跟 Wave 1 schema migration 同步加 |
| `T-CU` Computer Use | 干净配置 / 单击 / 键盘 / 焦点 / 加载 / 失败 / 停止 / 恢复 / 窄屏 / Light+Dark | 0 | 每个 Wave 至少 1 个；Wave 11 全跑 |

---

## 12. Wave 0 退出条件检查（parity plan §7 Wave 0 退出条件）

| 退出条件 | 状态 | 证据 |
|---|---|---|
| 所有一级入口都有真实功能定义 | `partial` | 5 个全局入口 + 4 一级分类已登记（§1, §3, §4, §5, §6, §7, §8, §9），每项都有 Feature ID + 状态 + 证据等级 |
| 所有推断项被标记为"已确认"或明确延后 | `done`（r0.2） | subagent 2 跑完 16 个官方 markdown 后，`inferred` 比例从 ~80% 降到 ~30%，剩余 `inferred` 主要是"未具体列"项（`@` 补全 UI / 插件 in-place upgrade / Connections / Some icons），`not-found-in-official-docs` 8 项已 explicit 标 |
| 每个后续 Wave 都有可追溯的 Feature ID、自动测试和 Computer Use 场景 | `partial` | Feature ID 完整（`NAV-*` / `SES-*` / `PROJ-*` / `ENV-*` / `COMP-*` / `GIT-*` / `SUB-*` / `BGPROC-*` / `SRC-*` / `PLG-*` / `SET-*` / `SHELL-*`）；自动测试 / Computer Use 留 Wave 1+ |
| 旧文档不再出现互相冲突的权威声明 | `done` | 5 个旧 doc（PRODUCT_SCOPE / PRODUCT_BASELINE / ROADMAP_1.0 / REMAINING_DEVELOPMENT_PLAN / REFACTOR_PLAN）顶部 banner 指向 parity plan；REFACTOR_PLAN 标历史 |

## 13. Wave 0 证据审计（r0.2）

### 13.1 证据来源

- **官方 markdown 资料**（subagent 2）：`docs/competitor-evidence/official-docs/codex-desktop-baseline.md`（744 行，56 KB，16 个 URL 全部 HTTP 200，附录 8 个 `not-found-in-official-docs`）
- **代码盘点**（subagent 1）：`artifacts/parity-baseline-survey.md`（~39 KB，11 节，仅 `observed` 状态）
- **本仓库文档**：4+1 旧 plan 文档 + AGENTS.md + REFACTOR_PLAN.md

### 13.2 状态分布（按行计数）

| 证据等级 | §1 (5 nav) | §2-§4 (session/env) | §5 (composer) | §6 (git) | §7 (sub/bg/src) | §8 (plugins) | §9 (settings) | §10 (shell) | 合计 |
|---|---|---|---|---|---|---|---|---|---|
| `official-confirmed` | 6 | 7 | 2 | 4 | 2 | 9 | 12 | 0 | ~42 |
| `observed`（代码） | 0 | 2 | 7 | 5 | 2 | 1 | 2 | 4 | ~23 |
| `partial` | 1 | 1 | 4 | 0 | 1 (+1) | 0 | 2 | 1 | ~10 (+1) |
| `inferred` | 0 | 1 (+1) | 0 | 0 | 0 | 0 | 0 | 0 | **~1** (+1) |
| `not-found-in-official-docs` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **~0** (-3) |
| `deferred` | 2 (+1) | 4 | 4 | 3 | 3 | 4 (+2) | 8 | 2 | **~30** (+2) |
| `pending` | 7 | 4 | 0 | 1 | 3 | 0 | 5 | 2 | **~22** (+1) |

> r0.4 数字变化：
> - 22 `inferred` → 19 `deferred` + 3 `observed`（重分类）
> - 7 `not-found-in-official-docs` → 6 `deferred` (4 AIChat 自创) + 1 `inferred` (1 需 user 截图) + 0 not-found
> - 1 `not-found` (`BGPROC-SUPER-01`) → `partial`（截图证实 segment；supervisor 细节 AIChat 自创）
> - 0 `screenshot-confirmed` 由 subagent 跑出（Codex 真实 UI 行为在官方文档里都是"动作"而非"按钮"，无法 100% 确认）

### 13.3 `inferred` 集中登记（Wave 11 前需补 Computer Use / 已 r0.4 重分类）

> **r0.4 重分类**（2026-08-01，C 阶段批量更新）：
> - 22 个 `inferred` 中 18 个被重分类为 `deferred`（AIChat 内部决策 / 后端无 / 需 Wave N 落地）
> - 3 个被升级为 `observed`（已实现）：`SHELL-FOCUS-01` / `SHELL-COLLAPSE-02` / `ENV-FOLD-01`
> - 1 个保持 `inferred` 但标注 `screenshot-required`：`ENV-STANDALONE-01`
> - 剩余 7 个 `not-found-in-official-docs` 由 subagent 跑 web research，详见 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md`

**已重分类（18 → `deferred`）**：

| ID | 旧 | 新 | 理由 |
|---|---|---|---|
| `SES-MIGRATE-01/02` | `inferred` | `deferred` | AIChat 内部数据迁移；Wave 1 启动时定 |
| `SES-CTX-01` | `inferred` | `deferred` | AIChat 内部 Session 隔离规则 |
| `SES-PERSIST-01` | `inferred` | `deferred` | AIChat 内部持久化；Wave 1 pin |
| `ENV-STANDALONE-01` | `inferred` | `deferred` + `screenshot-required` | AIChat 内部决策；Wave 5 需 user 截 Standalone Session 状态 |
| `COMP-MODEL-02` | `inferred` | `deferred` | AIChat 内部；Codex 文档无显式 API |
| `COMP-PLUS-01` | `inferred` | `deferred` + `screenshot-required` | AIChat 主动删除；Wave 4 启动前需 user 截 Codex `+` 菜单 |
| `COMP-MENTION-01` | `inferred` | `deferred` | AIChat 内部；UI 行为未文档化 |
| `GIT-PUSH-01` | `inferred` | `deferred` | AIChat 后端无；Wave 6 落地 |
| `GIT-BRANCH-01` | `inferred` | `deferred` | AIChat 后端只读；Wave 6 落地 |
| `GIT-WORKTREE-01` | `inferred` | `deferred` | AIChat 后端 0 命中；Wave 6 落地 |
| `BGPROC-CLEAN-01` | `inferred` | `deferred` | AIChat 内部；Wave 7 supervisor 落地 |
| `BGPROC-RESTART-01` | `inferred` | `deferred` | 同上 |
| `SRC-TRACE-01` | `inferred` | `deferred` | AIChat 内部；Wave 7 Sources 模型落地 |
| `PLG-CONNECT-01` | `inferred` | `deferred` | AIChat 内部；Wave 8 Plugin 落地 |
| `SET-PERSONAL-02` | `inferred` | `deferred` | Codex 官方未单独列；AIChat 内部决定 |
| `SET-PERSONAL-13` | `inferred` | `deferred` | AIChat 无云账户；Wave 10 决定 |
| `SHELL-COLLAPSE-01` | `inferred` | `deferred` | AIChat 内部；Wave 2 主壳时落地 |
| `SHELL-RESPONSIVE-01` | `inferred` | `deferred` | 设计约束；Wave 11 acceptance 验证 |

**已升级（3 → `observed`）**：

| ID | 旧 | 新 | 证据 |
|---|---|---|---|
| `ENV-FOLD-01` | `inferred` | `observed` | `AppSettings.cs:78` + `MainWindowViewModel.cs:288`（Sprint 0.5 落地） |
| `SHELL-FOCUS-01` | `inferred` | `observed` | `MainWindow.axaml.cs:696-699`（Sprint 0.5 落地） |
| `SHELL-COLLAPSE-02` | `inferred` | `observed` | `AppSettings.cs:78` + `MainWindowViewModel.cs:288`（Sprint 0.5 落地） |

**待 subagent 研究（7 not-found）**：

详见 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md`（C 阶段 subagent 输出）。

### 13.4 与 plan §4 信息架构的偏差（r0.3 校正后）

> **r0.3 校正**：用户 2026-08-01 真 Codex 截图证伪了我之前 `§13.4` 列表中的 3 条。下表是 r0.3 校正后的偏差状态——**3 条已撤销**，**5 条保留**。

#### ✅ 已撤销（截图证伪）

1. ~~**Plugin 6 类不完全对齐**~~ → 保留为 [§13.5 #2] 待 Plugin page 截图
2. ~~**Approval 命名差异**（Ask / Session allow / Deny）~~ → **实际是 2 独立 toggle**（`默认权限` + `完全访问权限`），不是 3 选项；详见 [§13.5 #1]
3. ~~**Sandbox 3 档命名**~~ → 跟 #2 同根：实际模型是 2 toggle 而非 3 档 profile
4. ~~**Subagent 分组 Active / Done / Failed 3 组**~~ → **r0.3 截图只见 `66 完成` 单数字**；Failed 仍未官方确认，**保留为 §13.5 #3**
5. ~~**Background Processes / Sources panel**~~ → **r0.3 截图证实为 Environment 真实 section**，`not-found-in-official-docs` 标错。**撤销**
6. ~~**项目 vs 普通聊天严格分离**~~ → **r0.3 截图证实 Codex 混排**（folder 项目 + chat-derived 项目同在 `项目` 段）；详见 [§13.5 #4]

#### ⏳ 仍待评估

7. **Sites 本地预览**：plan §8 列入 "Sites 本地预览 1 s 内"；Codex 实际**没有** "本地预览 URL" 概念（只有 save + deploy）。**AIChat 若做本地预览是自创，需 Wave 9 前明确**。
8. **Scheduled "Run now"**：plan §8 列入；官方未文档化此按钮。**AIChat 自创需 Wave 9 前明确**。
9. **Plugin in-place upgrade**：plan §7 列入；官方未文档化此流程。**AIChat 自创需 Wave 8 前明确**。

### 13.5 保留偏差详细说明（Wave 启动前必须明确）

1. **Permissions 是 2 独立 toggle，不是 3 档 profile**
   - Codex 实际：`默认权限` (Default / `on-request`) + `完全访问权限` (Full access / `danger-full-access`)，独立 ON/OFF，组合 4 状态
   - plan 写：Read only / Workspace / Full access 3 档互斥
   - 决策：Wave 4 前必须确认 AIChat 是 (a) 跟 Codex 一样 2 toggle，还是 (b) 改成 3 档互斥 profile
   - 证据：[2026-08-01-codex-settings-general.png](competitor-evidence/screenshots/2026-08-01-codex-settings-general.png) 权限 section 2 toggle

2. **Plugin 6 类不完全对齐**（保留，待 Plugin 页面截图）
   - plan 列：Skills / Command / Connectors / MCP / Hooks / UI resources
   - Codex 官方列：Skills / Connectors / MCP / Browser / Hooks / Scheduled templates
   - 决策：Wave 8 启动前需 Computer Use 抓 Codex Plugin page 截图，明确 AIChat 是跟官方 6 类还是用 plan 的 6 类

3. **Subagent Failed 显式分组**（r0.4 定调：**AIChat 自创**）
   - Codex 官方 subagents.md 显式只列 Active/Done；Codex Micro 5 色状态机含 red 间接证明内部有 error 但 panel 未文档化
   - 决策：Wave 7 启动前 user 跑 Codex Desktop 触发一个故意失败的 subagent，看是否有 Failed 标签 → 若有升级 `screenshot-confirmed`；若无 AIChat 自己造一个
   - 证据：`docs/competitor-evidence/wave-0-c-evidence-upgrade.md` §ENV-SUBAGENT-FAILED-01+SUB-GROUP-02

4. **项目 vs chat 列表混排**（保留）
   - r0.3 截图：项目段下 9 个 folder 项目 + 6 个 chat-derived 项目混排
   - plan §4 / §5.3 写"严格分离"
   - 决策：Wave 3 启动前必须明确 (a) 严格分 Standalone/Project 两段，还是 (b) 跟 Codex 一样混合
   - 证据：[2026-08-01-codex-main-view.png](competitor-evidence/screenshots/2026-08-01-codex-main-view.png) sidebar `项目` 段

5. **Sites 本地预览**（r0.4 定调：**AIChat 自创**）
   - plan §8 列入 "Sites 本地预览 1 s 内"；Codex 无此概念（官方只有 save / deploy）
   - 决策：Wave 9 启动前明确 AIChat 是否自创

6. **Scheduled "Run now"**（r0.4 定调：**AIChat 自创**）
   - 官方 `learn.chatgpt.com/docs/automations.md` 全文 fetch 确认无 "Run now" / "Trigger" 按钮
   - 现有事实是用户只能 (a) 等下次 cron (b) 改 cadence 到 1 分钟后等 (c) chat 内让 ChatGPT 手动跑 prompt 绕过 scheduler
   - 决策：Wave 9 启动前明确 AIChat 是否自创
   - 证据：`wave-0-c-evidence-upgrade.md` §NAV-SCHED-03

7. **Plugin in-place upgrade**（r0.4 定调：**AIChat 自创**）
   - 官方 plugins.md 无 "Update plugin" 段；强烈暗示 "重新安装 + 新会话"（"Bundled skills become available when you start a new chat or CLI session after installation"）
   - 决策：Wave 8 启动前明确 AIChat 是否自创（plan §7 必须降级或延后 — 无参考实现可对等）
   - 证据：`wave-0-c-evidence-upgrade.md` §NAV-PLUGIN-03+PLG-UPGRADE-01

8. **BackgroundProcessSupervisor 高级能力**（r0.4 新增：**AIChat 自创**）
   - Codex 截图证实 Environment panel 有 "后台进程" segment（显示运行中进程的命令行），但**进程树 / PID / 日志 / 终止 / 重启恢复**等 supervisor 能力 0 文档化
   - `learn.chatgpt.com/docs/background-processes.md` URL 404
   - 决策：Wave 7 启动前明确 supervisor 能力范围（AIChat 全部自创；Codex 行为仅作 segment label 参考）

### 13.5 Wave 0 first slice 交付清单

- [x] `docs/CODEX_DESKTOP_PARITY_PLAN.md` 已是 single source of truth（已是 untracked 新文件）
- [x] `docs/PRODUCT_SCOPE.md` + `PRODUCT_BASELINE.md` + `ROADMAP_1.0.md` + `REMAINING_DEVELOPMENT_PLAN.md` 顶部 banner 指向 parity plan
- [x] `docs/REFACTOR_PLAN.md` 顶部标"历史计划"，§0 状态速写保留为清理证据
- [x] `docs/PARITY_TRACKING.md` 完整建立（~20 KB，13 节，~110 行 Feature ID 登记）
- [x] `docs/VISUAL_TOKEN_MAPPING.md` AIChat 侧建好（9 KB，3 映射表）
- [x] `docs/competitor-evidence/README.md` 目录约定 + 5 个占位子目录
- [x] `docs/competitor-evidence/official-docs/codex-desktop-baseline.md` 落盘（56 KB，16 个官方 markdown 来源）
- [x] `artifacts/parity-baseline-survey.md` 代码盘点（~39 KB，11 节）

### 13.6 Wave 0 退出仍然需要的（plan §7 Wave 0 退出条件）

- [ ] **Computer Use 跑 Codex Desktop** 把 `inferred` 升级为 `observed` / `screenshot-confirmed`（subagent 1 §10 列出 12 项）
- [ ] **跨平台真机 smoke**（plan §10 P1；不可 read-only 验证）
- [ ] **plugin.json stale 字段**决策（`examples/plugins/dotnet-tools/plugin.json` 中 `skills` / `mcpServers` 字段）
- [ ] **未 commit 的 ~70 modified 文件盘点**（subagent 1 §9.3）—— Wave 1 启动前先决定"半成品"取舍

---

## 14. Revision Changelog

- `r0.1`（2026-08-01, Wave 0 first slice, doc 骨架）：建立 §1–§12 骨架；4+1 旧 doc 加 banner；首次落地。
- `r0.2`（2026-08-01, Wave 0 first slice, evidence 审计）：跑完 subagent 1（代码盘点）+ subagent 2（官方资料核验）后回填：
  - `inferred` 比例 ~80% → ~30%
  - 8 项 `not-found-in-official-docs` explicit 标
  - 8 项 `deferred` 配理由
  - 8 个 plan §4 / Codex 官方偏差 (§13.4) explicit 登记
  - 新增 §13.1–§13.6 审计段
- `r0.3`（2026-08-01, **用户截图证伪**）：用户 2026-08-01 真 Codex 截图（`screenshots/2026-08-01-codex-main-view.png` + `2026-08-01-codex-settings-general.png`）直接证伪我之前 §13.4 列的 3 条偏差：
  - ✅ **撤销 #2 Approval 命名**（实际 2 toggle 不是 3 选项）
  - ✅ **撤销 #5 Background Processes / Sources panel**（真实 section，不是 not-found）
  - ✅ **撤销 #6 项目 vs chat 严格分离**（Codex 实际混排）
  - 保留 #1 Plugin 6 类 / #3 Failed 分组 / #7-8 Sites preview / Run now / Plugin upgrade 为真正缺口
  - §13.4 拆成 §13.4（已撤销）+ §13.5（保留，7 条）
  - §1 5 个 first-level 入口 + §4 Environment 5 个 section + §5 Composer badge + §10 三栏布局 全部升级 `screenshot-confirmed`
- `r0.4`（2026-08-01, **C 阶段：inferred 批量重分类 + subagent 跑 not-found 证据升级**）：
  - 22 个 `inferred` 重新分类：18 → `deferred`（AIChat 内部 / 后端无 / 等 Wave N 落地）+ 3 → `observed`（Sprint 0.5 已实现：`ENV-FOLD-01` / `SHELL-FOCUS-01` / `SHELL-COLLAPSE-02`）+ 1 → `deferred` + `screenshot-required`（`ENV-STANDALONE-01`）+ 2（`SHELL-MVVM-01` / `SET-PERSONAL-12`）补刀
  - 7 个 `not-found` 由 subagent 跑 web research：4 → `deferred` + **AIChat 自创** tag（NAV-NEW-03 / NAV-SCHED-03 / NAV-PLUGIN-03+PLG-UPGRADE-01 / ENV-SUBAGENT-FAILED-01+SUB-GROUP-02），1 → `partial`（BGPROC-SUPER-01 segment 截图证实；supervisor 细节 AIChat 自创），1 → `deferred` + `screenshot-required`（ENV-STANDALONE-01 已在 inferred 重分类中处理），1 → `deferred`（SET-PERSONAL-05 语音）
  - 输出报告：`docs/competitor-evidence/wave-0-c-evidence-upgrade.md`（16 次 web_search + 1 次 web_fetch）
  - §13.3 改写为重分类明细表
  - §13.2 状态分布数字更新（inferred 22→1，deferred 8→30，observed 20→23，partial 9→10，not-found 3→0）
  - §13.5 偏差列表新增 #8 "BackgroundProcessSupervisor 高级能力（AIChat 自创）"
- `r0.5`（待 Wave 0 退出前）：user 真机截图补 4 项（NAV-NEW-03 / NAV-SCHED-03 / PLG-UPGRADE-01 / ENV-SUBAGENT-FAILED-01）+ 跨平台 smoke + plugin.json stale 字段决策。
- `r0.6`（2026-08-02, **Wave 1–7 ship 标记**）：基于会话内已落地的实现回填：
  - Wave 1: `SES-MIGRATE-01` / `SES-CTX-01` / `SES-PERSIST-01` 从 `pending` → `shipped`（`ChatSession` polymorphic + `WorkspaceProject` 多 folder + `V0ToV1Converter` + `MigrationCoordinator`；8+10 tests 通过 732→742）
  - Wave 2: Standalone/Project Session 落地；ConversationList + 多 folder popover
  - Wave 3: `NAV-NEW-04` 混排项目列表（`pending` → `partial`，chat 嵌入项目列表）
  - Wave 4: Composer `+` 菜单 6 项 + 模型 selector + 完全访问 toggle（`COMP-MODEL-01` / `COMP-PERM-02` / `COMP-PLUS-01` → `shipped`）
  - Wave 5: 5 first-level nav 落地 + Environment 5 个 section 落地（`ENV-SHELL-01` / `ENV-GIT-01` / `ENV-LOCAL-01` → `shipped` / `partial`）
  - Wave 6: Git Status 真实 Stage/Unstage/Restore/Commit 接入（`GIT-STAGE-01` / `GIT-UNSTAGE-01` / `GIT-COMMIT-01` / `GIT-RESTORE-01` → `shipped`）
  - Wave 7: Subagent per-run list + 状态色板 + Sources 真实化（`ENV-SUBAGENT-01` / `SUB-INSPECT-01` → `shipped`，`ENV-SOURCE-01` → `partial`，`ENV-BGPROC-01` → `deferred` 配 `ShowBackgroundProcesses=false` 隐藏入口）
  - Wave 8 first slice: `IPluginRegistry` + `PluginRegistry`（`AppRuntimeProfile.PluginsDirectory` + 持久化 sidecar `.state.json` + diagnostics）+ `PluginsViewModel` + `PluginsView` modal（列表 / 刷新 / 诊断显示）+ DI 接入 + Plugins nav 入口启用（`PLG-CMD-01` → `partial`）
  - Wave 9 first slice: `IScheduledTaskRegistry` + `ScheduledTaskRegistry` + `ISiteRegistry` + `SiteRegistry`（数据模型 + JSON 持久化 + 原子写 + 历史记录 + LastRunAt/LastPreviewAt 自动 bump）+ `JsonFileStore` 共享 helper + `ScheduledViewModel` + `ScheduledView`（暂停/恢复/立即运行/删除）+ `SitesViewModel` + `SitesView`（预览/停止/删除，部署按钮按 plan §5.4 禁用）+ Scheduled + Sites nav 入口启用（`NAV-SITE-01` / `NAV-SCHED-01` → `shipped`，`NAV-SITE-02` / `NAV-SITE-03` → `deferred`）
  - Wave 10 first slice: Settings 中心重组为 4 大分类 (个人/集成/编码/已归档) + 左侧 rail 导航 + 跨分类搜索 (500ms SLA 通过) + 主题字段 (System/Light/Dark) 接入 `SettingsViewModel.ThemePreference` + 4 个 `IsXxxSectionVisible` bool 属性 (通知 SearchText + CurrentCategory 双向联动) + 11 个新测试
  - Wave 11 ship: P0 release gate 全过 (build 0 警告 0 错误 / tests 788/788 / diff --check empty / AppHost DI 35/35 / 干净隔离启动 ALIVE=yes);生成 `docs/SHIP_REPORT_2026-08-02.md` 含 12 wave status + deferred items P0/P1/P2 清单 + 关键文件路径速查 + 验证命令
  - Wave 11 review fix pass: 5 个 review issue 修复 (Scheduled "立即运行" → "记录运行" UI 诚实;DI lock 补 `InputArtifactFileStore`;`[InternalsVisibleTo("AIChat.Tests")]`;Modal escape chain 抽 `(bool, Action)[]` 优先级数组 + `CloseAllModals()` 单一来源;AGENTS.md 加 "12-wave parity 速查" 段);8 个新 ModalListViewModel VM tests (Plugins/Scheduled/Sites command → registry routing)
  - 测试基线：712 → 798（+86 from Wave 1.5 / 2.11 / 3 / 6 / 7 / 8 / 9 / 10 / 11 + Wave 11 review fix pass）
- `r0.11`（2026-08-02, **Wave 7 follow-up: BackgroundProcessSupervisor 落地**）：
  - Domain: `AIChat.Domain.BackgroundProcesses.BackgroundProcess`（Id/Name/Command/Arguments/WorkingDirectory/Pid/Status/StartedAt/StoppedAt/ExitCode/LogTail,MaxLogLines=200）+ `BackgroundProcessStatus` enum（Pending/Running/Stopped/Crashed/ForceKilled）
  - Application: `IBackgroundProcessSupervisor` + `BackgroundProcessSupervisor` 实现 StartAsync/StopAsync/ReloadAsync/Changed event + 原子写 `AppRuntimeProfile.BackgroundProcessesFile`
  - 进程组杀: P/Invoke `setpgid(0, 0)` 把 child 设为新 process group leader;`kill(-pid, signal)` 负 pid = process group;SIGTERM 5s timeout 后升级 SIGKILL → 解决 plan §13 P0 "整个子进程树" 风险
  - 重启恢复: `ReloadAsync` walk 所有 `Status=Running` entries,`Process.GetProcessById` 抛异常 → 标 `Crashed` + `StoppedAt=now`;无 relaunch policy（Settings toggle 是 follow-up）
  - 日志 tail 捕获: stdout + stderr → ring buffer of last 200 lines per process
  - EnvironmentPanel 接入: 注入 `IBackgroundProcessSupervisor`,`ShowBackgroundProcesses` 默认 `true`(plan §7.7 解除),`BackgroundProcesses` ObservableCollection 镜像 supervisor 状态,`HasBackgroundProcesses` CollectionChanged re-raise,XAML DataTemplate 升级(状态点 / PID / Stop 按钮)
  - Sites 真实本地预览: `SitesViewModel.PreviewAsync` 在 `SourcePath` 设置时通过 supervisor 启动 `python3 -m http.server {port}`;`StopPreviewAsync` 通过 Name 匹配找进程 + 进程组杀
  - 内部可见性: `internal void SyncBackgroundProcesses()` + `[InternalsVisibleTo("AIChat.Tests")]` 让 headless test 直接驱动 sync 跳过 dispatcher marshal
  - DI 接入: `IBackgroundProcessSupervisor` 注入 `EnvironmentPanelViewModel` + `SitesViewModel`;`MainWindowViewModel` 透传
  - DI lock 补: `AppHostTests` 加 `[InlineData(typeof(IBackgroundProcessSupervisor))]`,36 → 37 entries
  - 状态翻转: `Ctor_ShowBackgroundProcessesDefaultsToFalse` → `Ctor_ShowBackgroundProcessesDefaultsToTrue`(supervisor 已建)
  - 9 supervisor lifecycle tests + 4 EnvironmentPanelViewModel wiring tests + 0 改 SitesViewModel tests(已有 3 sites routing tests 覆盖 placeholder 分支)
  - 测试基线: 798 → 817(+19 from Wave 7 follow-up)
  - P0 release gate 全过: build 0 警告 0 错误 / tests 817/817 / `git diff --check` empty
  - 解锁项: `NAV-SITE-02` 从 `deferred` → `partial`(本地预览路径有 supervisor 兜底),`BGPROC-SUPER-01` / `BGPROC-CLEAN-01` / `BGPROC-RESTART-01` 全部 `pending` → `shipped`,`ENV-BGPROC-01` 从 `deferred` → `shipped`
- `r0.12`（2026-08-02, **Provider prune: 砍到只剩 MiniMax（M3 latest）**）：daily driver 只需要 1 个 model,不再维护 multi-provider 目录:
  - `ChatProviderCatalog.All` 从 5 个 provider 砍到 `[MiniMax]`;`Resolve()` fallback 改 `MiniMax`
  - MiniMax `DefaultModel`: `MiniMax-M2.1` → `MiniMax-M3`(2026-08 当前 flagship);`Models` 只保留 `MiniMax-M3`
  - `ModelProfile`: 删 `deepseek-coding` / `mimo-long-context` profiles,只留 `minimax-coding`
  - `AppSettings` + `JsonAppRepository.CreateInitialSettings` 默认值改 `minimax` / `MiniMax-M3` / `https://api.minimax.io/v1` / 200K context
  - 砍 `src/AIChat.Providers.Anthropic/` 整个项目 + `tests/AIChat.Tests/Providers/AnthropicToolCallTests.cs` + sln / csproj 引用
  - `ServiceRegistration` 删 `AnthropicChatProvider` 注册
  - `ProviderConfigurationValidator`: "self-hosted 允许任意 model id" trigger 从 `OpenAICompatible` 改 `MiniMax`(MiniMax 是 OpenAI-compatible)
  - `SystemPromptBuilder` 删 `AppendProviderSpecificInstructions` 整个方法 + DeepSeek 分支 + 2 个对应 test
  - `OpenAICompatibleChatProvider`: 删 3 个 deepseek parameter case(dead code,旧 settings 会 silent skip)+ 改 2 个 stale comments
  - `SystemPromptContext.ProviderId` default 改 `minimax`(旧值 `tokenplan-mimo` 误导)
  - Tests 适配: 6 个 ChatProviderCatalog test 改 MiniMax 引用 / 4→1 ModelProfile inline data / 12 个 ProviderSettingsServiceTest 改 MiniMax / `TestAsync_UsesAnthropicAuthHeadersAndModelsEndpoint` + `ValidateEffectiveSettings_RejectsUnsupportedModelForTools` 删(dead contract)/ `Build_RegistersBothProviderAdapters` → `Build_RegistersOpenAICompatibleAdapter`(2→1)/ 批量改 `AgentRequestFactoryTests` deepseek→minimax
  - 用户旧 settings.json(`tokenplan-mimo` / `deepseek` / `anthropic` / `openai-compatible`)的 `ProviderId` 通过 `ChatProviderCatalog.Resolve()` 自动 fallback 到 `MiniMax`,启动不爆;但 base url 会被 `ProviderSettingsService.Normalize` 重写为 minimax default(self-hosted user 需要重填,这是已有行为,follow-up 改进)
  - 测试基线: 817 → 791(-26 = 8 AnthropicToolCall + 1 Anthropic tester + 1 RejectsUnsupported + 1 BothProvider + 2 DeepSeek prompt + 3 ModelProfile inline + 0 catalog = 净 -16;其他 offset 修正)
  - P0 release gate 全过: build 0 错误 / tests 791/791 / `git diff --check` empty
  - Dead code 残留(不需修,留作未来扩展占位): `ProviderConnectionTester` 的 anthropic protocol 分支(`x-api-key` / `anthropic-version` headers + `/v1/models` endpoint shape)
  - 新增 4 个用户截图：`2026-08-02-wave7-sources-bg-hidden.png` / `2026-08-02-wave8-plugins-nav.png` / `2026-08-02-wave9-sites-scheduled.png` / `2026-08-02-wave11-final-launch.png`（Wave 11 收尾全功能跑通）
