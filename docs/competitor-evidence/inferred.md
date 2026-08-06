# Inferred 证据集中登记

> **r0.4 更新（2026-08-01）**：本表与 `docs/PARITY_TRACKING.md` §13.3 已同步。**22 个 `inferred` 已批量重分类**（18 → `deferred` + 3 → `observed` + 1 → `deferred + screenshot-required`）。本表底部的「集中状态」也对应更新为 0 inferred。
> 
> 详细重分类明细见 `docs/PARITY_TRACKING.md` §13.3。7 个 `not-found-in-official-docs` 的证据升级输出在 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md`（C 阶段 subagent 输出）。
>
> ---
>
> **历史说明**（r0.4 之前）：本表曾集中登记 `docs/PARITY_TRACKING.md` 中所有标 `inferred` 的项，每条列 (a) Feature ID、(b) 推断理由、(c) 升级为 `observed` / `screenshot-confirmed` 所需动作。r0.4 之后这个分类**不适用**了——下面保留旧内容做历史参考，但**当前证据等级请以 `PARITY_TRACKING.md` 为准**。

## A. 来自 §1 全局入口

### `NAV-NEW-03` 普通聊天移动/复制到项目
- 推断理由：`projects.md` 仅在 web 段提 "move it into a project"，无具体 UI 步骤。
- 升级动作：Computer Use 跑 Codex Desktop，录"项目右键菜单 / chat 三点菜单"是否有 move / copy 选项。

### `NAV-PR-01/02/03` PR 列表 / 创建 / 详情
- 推断理由：官方 markdown 确认 PR 上下文进入 sidebar + review pane；**没有**"独立 PR 一级入口"的明确文档化（PR 集成在 code review 流程内）。
- 升级动作：Computer Use 截 sidebar 找 PR 入口；wave 6 启动前明确"独立 PR 一级入口"是否 AIChat 自创。

## B. 来自 §2 Session

无 `inferred` 项（均已 `official-confirmed` 或 `observed`）。

## C. 来自 §3 Project

### `PROJ-ADD-01` folder picker 流程
- 推断理由：⌘O 走 Avalonia `AvaloniaProjectPicker`，**多 folder / Primary 切换** UI 步骤未文档化。
- 升级动作：Wave 3 启动前用 Computer Use 跑 Codex Desktop 验证 multi-folder / primary 切换的步骤数。

## D. 来自 §4 Environment

### `ENV-STANDALONE-01` Standalone 隐藏项目/Git 区块
- 推断理由：Codex 官方未明确"Standalone Session 不显示 Environment 面板"，但概念上普通 chat 不应显示。
- 升级动作：Computer Use 截普通 chat 状态下的 Environment 面板区域。

### `ENV-SOURCE-01` Sources 区块
- 推断理由：Codex 官方 Sources section 在 web 项目视图，**不是** desktop Environment 面板。
- 升级动作：Computer Use 截 desktop Environment 面板，验证是否真有 Sources section。

### `ENV-FOLD-01` 面板折叠状态持久化
- 推断理由：未官方文档化。
- 升级动作：Computer Use 验证面板折叠后重启是否保持。

## E. 来自 §5 Composer

### `COMP-MODEL-02` 推理等级
- 推断理由：官方 automations.md 提 "reasoning effort" 字段，但 Composer 入口未文档化。
- 升级动作：Computer Use 截 Composer 周边找推理等级切换。

### `COMP-PLUS-01` `+` 菜单（Source / Plugin 入口）
- 推断理由：AIChat 主动删了 `+` 菜单；plan §7 Wave 4 要求重建。
- 升级动作：Wave 4 启动前用 Computer Use 截 Codex Desktop Composer 找 `+` 菜单内容。

### `COMP-MENTION-01` `@` 补全菜单
- 推断理由：Codex 用 `@` 提 skill/plugin，但补全菜单形态未文档化。
- 升级动作：Computer Use 验证 `@` 键触发后的补全菜单 UI。

## F. 来自 §6 Git

### `GIT-PUSH-01` 推送
- 推断理由：AIChat 后端无 push；plan §7 Wave 6 要求。
- 升级动作：Wave 6 启动前用 Computer Use 截 push 入口（review pane？composer 下方？）。

### `GIT-BRANCH-01` 分支切换 UI
- 推断理由：Codex review pane 有 "Branch" view（对比 base branch diff），但**创建/切换**分支的 UI 未文档化。
- 升级动作：Computer Use 验证。

### `GIT-WORKTREE-01` Worktree 操作
- 推断理由：Codex 官方 markdown 提 "codex-managed / permanent" 概念，但创建 UI 未文档化。
- 升级动作：Computer Use 截"三点菜单 → Create permanent worktree"路径。

## G. 来自 §7 Subagents / BG / Sources

### `SUB-STOP-01` 单个 Subagent 停止控件
- 推断理由：Codex 官方 app 段提 "Ask Codex directly to steer...stop"，**没**直接说"在 UI 上点按钮停止"。
- 升级动作：Computer Use 截 Subagents panel 验证停止交互。

### `BGPROC-*` 全系列
- 推断理由：Codex 官方 markdown 无 Background Processes 页面。
- 升级动作：Computer Use 截 Codex Desktop 找 Background Processes 入口（status bar / Environment / separate panel）。

### `SRC-MODEL-01` / `SRC-TRACE-01` Sources 模型
- 推断理由：Codex web 项目视图有 Sources section，desktop 未明确。
- 升级动作：Computer Use 截 desktop Sources 入口。

## H. 来自 §8 Plugins

### `PLG-CONNECT-01` Connector OAuth UI
- 推断理由：官方 plugins.md 提 "connect it when prompted"，**没**具体 OAuth UI 步骤。
- 升级动作：Computer Use 截 OAuth 流程（browser popout / in-app / 外部）。

### `PLG-UI-01` 可选 UI resources
- 推断理由：plan §7 Wave 8 列；官方 markdown 未列。
- 升级动作：Wave 8 启动前决定"是否需要 UI resources 能力"——plan §5.4 也不强调。

## I. 来自 §9 Settings

### `SET-PERSONAL-02` 导入
- 推断理由：plan 提"导入"，官方 markdown 未明确。
- 升级动作：Computer Use 找 Settings → General → Import 入口。

### `SET-PERSONAL-12` 使用情况 / 计费
- 推断理由：AIChat 有 run summary token 数；plan 要求"使用情况 + 计费"；官方 Profile 段提"activity insights / lifetime tokens / streaks"——但**计费**官方未明确（Codex 自身是 ChatGPT 订阅）。
- 升级动作：Wave 10 启动前 AIChat 决定"是否做计费页"（如果做，需从 Provider API 拉数据）。

### `SET-PERSONAL-13` 账户 / 隐私
- 推断理由：AIChat 无云账户；plan 沿用 Codex 命名但意义不同。
- 升级动作：Wave 10 启动前 AIChat 决定"账户 = Keychain 持有凭据 + Provider 列表"。

## J. 来自 §10 主壳

### `SHELL-3COL-01` 三栏布局视觉
- 推断理由：plan §4 描述三栏（左 nav / 中 session / 右 env）；Codex 官方截图未直接描述。
- 升级动作：Computer Use 截 Codex Desktop 全景。

### `SHELL-FOCUS-01` 启动自动 focus
- 推断理由：plan §7 Wave 2 退出条件；官方未明确"启动后 Composer 焦点"是否自动。
- 升级动作：Computer Use 验证。

### `SHELL-COLLAPSE-01` sidebar 折叠
- 推断理由：plan §7 Wave 2 退出条件；官方 markdown 未提"sidebar 折叠"具体 UI。
- 升级动作：Computer Use 验证。

### `SHELL-RESPONSIVE-01` 响应式
- 推断理由：plan 要求"宽屏 / 窄屏 / Light / Dark 无重叠"；官方未文档化。
- 升级动作：Computer Use 拖窗口验证。

### `SHELL-MVVM-01` 拆 MainWindowViewModel
- 推断理由：plan §5.2 / Wave 2 要求；纯架构决策。
- 升级动作：Wave 2 启动时实施。

---

## 集中状态（r0.4 更新后）

| 类别 | `inferred` 数（r0.3） | `inferred` 数（r0.4） | 变化 |
|---|---|---|---|
| §A 全局入口 | 1 | 1 | 维持（NAV-NEW-03 为 `not-found`，不属于 inferred 桶） |
| §B Session | 0 | 0 | — |
| §C Project | 1 | 0 | 重分类为 `observed` / `deferred` |
| §D Environment | 3 | 0 | 2 → `deferred`（AIChat 内部 / 需 screenshot），1 → `observed`（`ENV-FOLD-01` Sprint 0.5 落地） |
| §E Composer | 3 | 0 | 2 → `deferred` + 1 → `deferred + screenshot-required` |
| §F Git | 3 | 0 | 全部 → `deferred`（AIChat 后端无） |
| §G Sub/BG/Src | 4 | 0 | 全部 → `deferred`（Wave 7 内部） |
| §H Plugins | 2 | 0 | 全部 → `deferred`（Wave 8 内部） |
| §I Settings | 3 | 0 | 全部 → `deferred`（AIChat 内部决策 / 无云账户） |
| §J 主壳 | 5 | 0 | 2 → `observed`（Sprint 0.5 落地），3 → `deferred`（Wave 2 内部） |
| **合计** | **25** | **0** | **-25（重分类）** |

> 注：上表数字包含本表 r0.3 列出但 `PARITY_TRACKING.md` 中标为其他状态的项（如 `ENV-SOURCE-01` 实际是 `screenshot-confirmed`、`SUB-STOP-01` 实际是 `official-confirmed`）。这是 r0.3 时期本表 vs tracking table 不一致导致的，r0.4 已统一以 tracking table 为准。
>
> 升级到 `observed` / `screenshot-confirmed` 主要靠 **Computer Use 跑 Codex Desktop**（r0.4 仍需 user 提供 3 项真机截图：`ENV-STANDALONE-01` / `COMP-PLUS-01` / Codex `+` 菜单）+ **subagent 跑 7 项 not-found web research**。

## r0.4 修正：旧 inferred 列表中已无 "inferred" 状态的项

旧本表（r0.3 及更早）列出但实际在 `PARITY_TRACKING.md` 中是**其他状态**的项：

| 旧 ID | 旧本表标 | 实际状态（r0.4 tracking table） | 原因 |
|---|---|---|---|
| `ENV-SOURCE-01` | inferred | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](screenshots/2026-08-01-codex-main-view.png) — `来源 codex-clipboard-e7da29ff...`) | r0.3 用户截图证实 |
| `SUB-STOP-01` | inferred | `official-confirmed` ([codex-desktop-baseline.md §6.1](official-docs/codex-desktop-baseline.md#61-active--done--failed-分组)) | 官方 markdown "Ask Codex directly to steer...stop" 段已确认 |
| `PROJ-ADD-01` | inferred | `observed`: `MainWindow.axaml.cs` ⌘O 绑定 | AIChat 内部已实现 |
| `SRC-MODEL-01` | inferred | `partial` ([codex-desktop-baseline.md §9.5](official-docs/codex-desktop-baseline.md#95-sources) — web 项目视图有，desktop 未文档化) | 部分官方支持 |
| `SHELL-3COL-01` | inferred | `screenshot-confirmed` ([2026-08-01-codex-main-view.png](screenshots/2026-08-01-codex-main-view.png) — 三栏布局) | r0.3 用户截图证实 |
| `SHELL-MVVM-01` | inferred | `deferred` (Wave 2 内部) | r0.4 重分类 |
| `SET-PERSONAL-12` | inferred | `deferred` (Codex 官方有 lifetime token / streaks；AIChat 内部有 token 但无计费 API) | r0.4 重分类 |

> 7 项旧 "inferred" 实际状态都已落到 `screenshot-confirmed` / `official-confirmed` / `observed` / `partial` / `deferred` 中的一种，r0.4 已在 `PARITY_TRACKING.md` 统一记录。
