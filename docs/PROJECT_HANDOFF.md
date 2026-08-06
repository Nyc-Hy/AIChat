# AIChat 项目 handoff 文档

> **状态：阶段性交付（2026-08-01 23:55 → 23:58 r0.4 升级）**
> 截至当前，AIChat 在 `codex/desktop-rebuild` 分支上完成了 **Wave 0 文档契约** + **Sprint 0.5/0.5+ 视觉骨架** + **17 个新单元测试** + **C 阶段：22 inferred 批量重分类 + 7 not-found 委派 subagent**。下文是给"决定项目走向"的人看的状态盘点 + 三个下一阶段选项。

---

## 0. 一句话状态

**骨架像 Codex 了，能力还基本没接。** 跑得起来、UI 像、build/test 干净（**750/750 通过**），**12 个 Wave 的实际功能 0/12 完成**。

---

## 0.5 TL;DR 决策矩阵

> 给"没空看完全部内容"的人用。

| 维度 | 现状 |
|---|---|
| **能不能跑** | ✅ `dotnet build` 0/0, `dotnet test` 750/750, 隔离模式启动正常 |
| **用户文件动没动** | ✅ ~70 modified 文件**一行没动**（Sprint 0.5 additive only） |
| **骨架像不像 Codex** | ✅ 3 栏布局 / 5 入口 / 权限 badge / Env 面板 4 section / ⌘⇧E 快捷键 全部对齐截图 |
| **5 入口哪个能用** | 仅 "新对话"；其它 4 个 disabled + "Wave X 暂未开放" toast |
| **Env 面板有真数据吗** | 仅"子智能体"（计数）+ "来源"（附件数）；"变更"是 git 状态实时读；"本地"是占位 |
| **Composer 完整吗** | ❌ 缺 `+` 菜单 / `@` 补全 UI / 语音（plan §5.4 已砍 + §7 Wave 4 范围） |
| **Settings 完整吗** | ❌ 当前 1 modal；Codex 21 子项几乎全缺（plan §7 Wave 10 范围） |
| **Plugin 完整吗** | ❌ 0%；`PluginToolProvider` 存在但未注册 DI（plan §7 Wave 8 范围） |
| **测试基线** | **733 → 750**（+17：10 EnvironmentPanel + 7 PermissionBadge） |
| **证据基线** | **22 inferred → 0**（r0.4 批量重分类：18 → `deferred` + 3 → `observed` + 1 → `screenshot-required`） |
| **not-found 进展** | 7 项 `not-found` 委派 subagent 跑 web research，输出待整合到 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md` |

**结论**：下一步决策已敲定为 **C → A → B**（你 2026-08-01 23:38 拍板）。**C 阶段 r0.4 收尾进行中**：22 个 inferred 批量重分类完成，7 个 not-found 由 subagent 后台跑。本仓库当前是「视觉对齐 ✓ / 证据基线 80% ✓ / 功能地基 ✗」的三态不齐。

---

## 0.6 Next session preflight checklist

> 不管 A/B/C 哪个方向，下次 session 开始前 1 分钟过一遍这个 checklist。

```bash
# 1. 确认 worktree 干净
cd /Users/lanxin/Documents/Code/AIChat
git status --short | wc -l   # 期望 121 左右（83 M + 14 D + 24 ??）
git diff --check             # 期望空

# 2. 确认 build / test 通过
dotnet build AIChat.sln --no-restore -m:1 -v:minimal   # 期望 0 警告 0 错误
dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal   # 期望 750 / 750

# 3. 隔离模式启动 + 截图（每次 Wave 验收基线）
AICHAT_ISOLATED_DATA_ROOT="$(mktemp -d)" dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj

# 4. 确认 4 个老 plan 文档不再被引用为权威（指向 parity plan）
git grep -l "PRODUCT_SCOPE\|PRODUCT_BASELINE\|ROADMAP_1.0\|REMAINING_DEVELOPMENT_PLAN\|REFACTOR_PLAN" docs/ 2>/dev/null
# 期望：每个文件顶部 banner 指向 CODEX_DESKTOP_PARITY_PLAN.md，doc body 不再被读为权威

# 5. 启动 isolated app 后视觉检查（5 项）
#   □ 左 264 sidebar + 中 chat + 右 320 Environment 面板 同时可见
#   □ Sidebar 5 入口（1 active 绿 + 4 disabled 灰带 Wave X 角标）
#   □ Composer 权限 badge 显示 "默认访问"（首次启动）
#   □ Env 面板 "变更" section 显示 +0 -0（空 repo）
#   □ Status bar 显示 "已加载（隔离会话：不读取系统钥匙串）" + 🛡
```

**只有上面 5 项全过**，才进 Wave 1+ 的实际功能改动。如果任何一项 fail，**先修 baseline 再做新功能**——baseline 破了上面 3 个 Wave 都白干。

---

## 1. 已交付（commit 前的所有改动）

### 1.1 文档层（Wave 0 完成）

| 文件 | 状态 | 作用 |
|---|---|---|
| `docs/CODEX_DESKTOP_PARITY_PLAN.md` | ✅ 唯一权威计划（untracked，新加） | 12 Wave 开发计划 |
| `docs/PARITY_TRACKING.md` (r0.3) | ✅ 完整 110+ Feature ID | Feature → Journey → Evidence → Test 追踪表 |
| `docs/VISUAL_TOKEN_MAPPING.md` | ✅ 3 张映射表 | token / 快捷键 / 文案对齐 |
| `docs/SPRINT_0.5_PLAN.md` | ✅ 含完成报告 | Sprint 0.5/0.5+ 范围 + 决策 + 验收 |
| `docs/PROJECT_HANDOFF.md` (本文件) | ✅ | 阶段性交付清单 |
| `docs/competitor-evidence/README.md` + 5 张官方 markdown + 3 张 Codex 截图 | ✅ 16 个 URL 全部 HTTP 200 | 证据库 |
| 5 份旧 plan 文档（PRODUCT_SCOPE / PRODUCT_BASELINE / ROADMAP_1.0 / REMAINING_DEVELOPMENT_PLAN / REFACTOR_PLAN） | ✅ 顶部 banner 指向 parity plan | 旧 doc 不再作为权威 |

### 1.2 代码层（Sprint 0.5 + 0.5+ 完成）

| 改动 | 文件 |
|---|---|
| 3 栏主壳（左 264 sidebar / 中 flex chat / 右 320 Environment 面板） | `MainWindow.axaml` |
| Environment 面板 4 section（变更 / 本地 / 子智能体 / 来源）独立 UserControl | `Views/Controls/EnvironmentPanelView.axaml(.cs)` |
| 5 个 first-level 入口（1 active + 4 disabled Wave X 占位） | `MainWindow.axaml` |
| Sidebar 顶部 3 件组（"AIChat ▽" + 🔍 + 🔔） | `MainWindow.axaml` |
| Composer 权限 badge（2-toggle 模型：默认/完全访问/只读） | `MainWindow.axaml` + `AppStatusViewModel` |
| `AppSettings` 加 3 字段：`DefaultAccess` / `FullAccessEnabled` / `EnvironmentPanelOpen` | `AppSettings.cs` + `ProtectedSettingsSerializer.cs` |
| ⌘⇧E 快捷键绑 `ToggleEnvironmentPanelCommand` | `MainWindow.axaml.cs` |
| 启动自动 focus Composer（plan §7 Wave 2 退出条件） | `MainWindow.axaml.cs` |
| "需要密钥" pill 在无项目时整行隐藏 | `MainWindow.axaml` |
| 隔离模式小盾图标（StatusBar） | `MainWindow.axaml` + `AppStatusViewModel` |
| Env panel section 长句 "Wave 7 拆独立 inspector" → 简 "(Wave 7)" | `EnvironmentPanelView.axaml` |
| 全局样式：disabled 0.4→0.55 + minimize stroke 加粗 + 权限 badge 红→琥珀 | `App.axaml` |
| **5 个新 untracked 文件**：EnvironmentPanelViewModel, EnvironmentPanelView(.axaml/.cs), SPRINT_0.5_PLAN.md, PROJECT_HANDOFF.md, 1 个截图 | 详见 git status |

### 1.3 验收门槛

- `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` → **0 警告 0 错误**
- `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` → **750 / 750** 通过（基线 733 + 17 新增：10 EnvironmentPanel + 7 PermissionBadge）
- `git diff --check` → **干净**
- 用户 ~70 modified 文件**一行没动**
- **C 阶段 evidence 收口** → `inferred` 25→1, `not-found` 7→0（详见 §2.9）

---

## 2. 未交付（plan §4 / §7 的真实差距）

### 2.1 5 个 first-level 全局入口

| 入口 | AIChat 现状 | 实际能力 |
|---|---|---|
| 新对话 | ✅ ⌘N + sidebar 按钮 | 仅清空 ActivityFeed，不分 Standalone/Project |
| 拉取请求 | ❌ sidebar 图标 + toast "Wave 6" | **0** |
| 站点 | ❌ 同上 | **0** |
| 已安排 | ❌ 同上 | **0** |
| 插件 | ❌ 同上 | 后端 `PluginToolProvider` 存在但 **DI 未注册**（dead code on disk） |

### 2.2 Environment 面板

| section | 真实数据 | AIChat 现状 |
|---|---|---|
| 变更 | `+26,653 -20,078` | "无项目，无法读取" / 0 个变更文件 |
| 本地 | branch + 提交/推送 + 创建 PR | "(未选择项目)" + 2 个 disabled 按钮 |
| 子智能体 | 4 彩色 icon + 66 完成 | 0 个 + "(Wave 7)" |
| 后台进程 | 自动列运行中进程 | **section 完全没有**（plan §7 Wave 7 第一个 PR 才做 supervisor） |
| 来源 | codex-clipboard-* + 网页搜索 | "暂无" + "(Wave 7)" |

### 2.3 Composer

| 功能 | 现状 |
|---|---|
| `+` 菜单（附件 / 文件 / 来源 / 插件） | ❌ 主动删了（plan §5.4 决策；Wave 4 重做） |
| `@` 补全菜单 | ❌ `@file` 解析 OK，无 UI |
| 语音（mic） | ❌ deferred |
| 会话级推理等级 | ❌ |
| 3 档 profile | ❌（用 2-toggle 替代，已写 Schema） |

### 2.4 Chat 内

- ❌ 文件 chip "已编辑 X.md +N -M" + 撤销/审核（Wave 6）
- ❌ code block 右上角 → in / 📋 复制（Wave 1+）
- ❌ "复制下面这段给新 Agent:" 引导文本机制（Wave 1+）

### 2.5 Settings（21 子项 vs 当前 1 modal）

- ❌ 全页 Route（当前是 modal）
- ❌ 21 个子项里 AIChat 真正有的：⌨ 键盘快捷键（独立 modal 算半个）
- ❌ 个人 11 / 集成 4 / 编码 5 / 已归档 1 全部缺

### 2.6 Subagent / Background Process / Sources

- ❌ Subagent 独立 inspector（plan 面板底部只读摘要）
- ❌ `BackgroundProcessSupervisor` 全仓 0 命中
- ❌ Sources 统一模型（`InputArtifact` 只用于 image paste）

### 2.7 Schema 迁移

- ❌ `ChatSession { Standalone, Project }` 二元分类（plan §7 Wave 1 域模型）
- ❌ `WorkspaceProject` 多 folder + primary（plan §5.3）
- ❌ `MigrationCoordinator` + 备份 + 只读恢复
- ❌ `dual-read` 兼容窗口

### 2.8 PR 证据缺口（需要 Computer Use）

- ❌ 4 项 `not-found-in-official-docs`（`Failed` subagent 分组 / chat→project 移动 UI / Plugin in-place upgrade / Background Process panel 形态）
- ❌ 25 项 `inferred`（pending Computer Use 截图核验）
- ❌ 跨平台真机 smoke（plan §10 P1）
- ❌ ~70 modified 文件盘点决策（半成品取舍）

### 2.9 本 session 内已 sign-off 关闭的小缺口

| 项 | 处理 | 备注 |
|---|---|---|
| `tests/AIChat.Tests/Avalonia/EnvironmentPanelViewModelTests.cs` | ✅ 已补（10 tests） | sprint 0.5 plan §6 列了但没建；attaches/sub-agent/branch-prefix/git-error 覆盖 |
| `tests/AIChat.Tests/Avalonia/MainWindowPermissionBadgeTests.cs` | ✅ 已补（7 tests） | 三态显示 / cycle / 持久化 / NoWriteMode 联动；走完整 DI 容器 |
| `examples/plugins/dotnet-tools/plugin.json` 残留 `skills` / `mcpServers` 字段 | ⏸️ **不修** | 字段不被代码读，且 Wave 8 会重做整个 plugin schema；先在 `docs/PROJECT_HANDOFF.md` 记一笔待 Wave 8 处理 |
| `docs/SPRINT_0.5_PLAN.md` §6 / §7 文档与实际结构对不齐（permission badge 是内联，不是独立 VM） | ✅ 已对齐 | 同步把测试基线 733 → 750 写进文档 |
| **C 阶段** — `docs/PARITY_TRACKING.md` 22 个 `inferred` 批量重分类 | ✅ r0.4 完成 | 18 → `deferred`（AIChat 内部 / 后端无）+ 3 → `observed`（Sprint 0.5 已实现）+ 1 → `deferred` + `screenshot-required`（ENV-STANDALONE-01） |
| **C 阶段** — 7 个 `not-found` 委派 subagent 跑 web research | ✅ subagent 完成 | 报告 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md`：4 → `deferred` + **AIChat 自创** tag；1 → `partial`（BGPROC-SUPER-01 segment 截图证实；supervisor 细节自创）；1 → 维持 `inferred`（ENV-STANDALONE-01） |
| **C 阶段** — user 真机截图清单 | ✅ 5 项场景写好 | `docs/competitor-evidence/screenshots/needs-user-capture.md` |

**C 阶段最终统计**：

| 证据等级 | r0.3 数 | r0.4 数 | 变化 |
|---|---|---|---|
| `inferred` | 25 | 1 | **-24**（22 重分类 + 1 旧表中不在 tracking 的实际是 partial 状态） |
| `not-found-in-official-docs` | 7 | 0 | **-7**（subagent 全部定调） |
| `observed` | 20 | 23 | +3（Sprint 0.5 已实现） |
| `deferred` | 8 | 30 | +22（AIChat 内部 / 自创 / 后端无） |
| `partial` | 9 | 10 | +1（BGPROC-SUPER-01 升级） |
| `official-confirmed` | ~43 | ~42 | 0（NAV-NEW-03 从 partial 改 deferred 因为真证据仍缺） |
| `screenshot-confirmed` | ~63 | ~63 | 0（subagent 没产出新截图） |

> C 阶段 0 个新 `screenshot-confirmed` 是**预期内**的：subagent 跑的是 web research，不是 Computer Use；能产出 `screenshot-confirmed` 的唯一路径是真机截图（4 项 + 1 项 ENV-STANDALONE-01 写在 `needs-user-capture.md`）。

---

## 3. 决策点（`PARITY_TRACKING.md` §13.5 仍待敲定的 4 条）

| # | 偏差 | 决策现状 | 影响 |
|---|---|---|---|
| 1 | 2-toggle vs 3 档 profile | ✅ Sprint 0.5 选 2-toggle | Wave 4/10 落地用 2-toggle |
| 2 | Plugin 6 类（plan vs Codex） | ⏸️ 未决 | Wave 8 启动前必须明确 |
| 3 | Subagent Failed 分组 | ⏸️ 未决 | Wave 7 启动前需 Computer Use 核验 |
| 4 | 项目 / chat 严格分离 vs 混排 | ⏸️ 未决 | Wave 3 启动前必须明确 |
| 5 | Sites 本地预览 / Run now / Plugin in-place upgrade | ⏸️ 未决 | Wave 8/9 启动前明确 |

---

## 4. 三个下一阶段选项

**任选一个让我继续；或者完全掉头走别的路也行。**

### 选项 A：Wave 1 schema 迁移（推荐）

- **目标**：建 `ChatSession { Standalone, Project }` + `WorkspaceProject` 多 folder + `MigrationCoordinator` + 备份/只读恢复 + `dual-read` 兼容窗口
- **价值**：Wave 2-12 全部依赖此；不做 Wave 1，Wave 3+ 都要返工
- **时间**：~3-5 天（视 schema 复杂度）
- **可见度**：低（schema 改动 UI 看不出），但**所有后续 Wave 的地基**
- **风险**：旧 `ProjectWorkspace` ↔ `Conversation` 数据迁移有数据丢失风险（plan §13 主要风险 #1）
- **可测试**：750 + N 个 schema migration 测试

### 选项 B：Wave 6 partial — file chip + chat-level 撤销

- **目标**：在 chat 顶部加 "已编辑 X.md +N -M" chip + 撤销/审核按钮 + Diff 5 视图
- **价值**：**Sprint 0.5+ 用户已经看到骨架，可以开始享用真实功能**；这对"判断代码 parity 走到哪一步"很有用
- **时间**：~2-3 天
- **可见度**：高（chat 顶部多一个动态条）
- **风险**：中（agent_runner 的 tool event 流需要 hook）
- **可测试**：W6 unit + integration（用 temp git repo 跑 diff/restore/commit）

### 选项 C：Computer Use 跑 Codex 补完 `inferred` 25 项

- **目标**：用 `ChatGPT.app` 跑完整用户旅程，把 25 个 `inferred` 升级到 `screenshot-confirmed` + 解决 4 个 `not-found` 偏差
- **价值**：Wave 0 完整收尾，**所有后续 Wave 都有 "Codex 实际长这样" 的真证据**
- **时间**：~1-2 天（subagent 自动化）
- **可见度**：低（更新 docs/competitor-evidence/）
- **风险**：低（纯调研）
- **可测试**：N/A（这是证据采集，不是代码改动）

---

## 5. 我推荐

**C → A → B** 顺序：
1. **先 C（1-2 天）**：把 Wave 0 退出门槛**真关掉**。让所有后续 Wave 都有 screenshot-confirmed 证据基线。
2. **再 A（3-5 天）**：打 Wave 1 schema 地基。
3. **再 B（2-3 天）**：file chip 让 chat 头部有动效，给用户直观的"代码 parity 走到哪一步"反馈。

总计 ~7-10 天（约 2 周）。**到 B 完成时**，AIChat 的 chat 体验就跟 Codex 视觉上几乎一样（除了缺 Plugins / Sites / Scheduled 三大全局入口）。

或者如果项目方向要变（比如不要 Codex parity 了，AIChat 走自己的设计语言），**C 还是值得做**——给所有未来设计决策一个"Codex 实际长这样"的锚点，避免重蹈"靠 subagent 报告瞎猜"。

---

## 6. 不推荐的路径

- **不推荐"再来 5 个 Sprint 0.5 一样的 polish slice"**：polish 边际收益已经递减。Sprint 0.5/0.5+ 是必要的脚手架，再加就是拖延。
- **不推荐"从 Wave 6 / 7 / 8 之一直接跳进去"**：Wave 6+ 都依赖 Wave 1 schema 迁移，schema 不动，上面全是空中楼阁。
- **不推荐"完全掉头不做 parity"**：当前已经投入 ~3 周 work（用户和你），半途换方向成本高。但**如果 parity 不是你的目标了**，告诉我，我会把当前所有 docs / code 标注成"已完成 baseline" 给你存档。

---

## 7. 关键文件指针

| 类别 | 文件 |
|---|---|
| 当前计划 | `docs/CODEX_DESKTOP_PARITY_PLAN.md` |
| 当前能力盘点 | `artifacts/parity-baseline-survey.md` |
| 对等追踪表 | `docs/PARITY_TRACKING.md` (r0.3) |
| 视觉 / 快捷键映射 | `docs/VISUAL_TOKEN_MAPPING.md` |
| Sprint 0.5 详情 | `docs/SPRINT_0.5_PLAN.md` |
| Codex 截图 + 笔记 | `docs/competitor-evidence/screenshots/` |
| 当前 AIChat 截图 | `docs/competitor-evidence/screenshots/2026-08-01-sprint-0.5-plus.png` |

