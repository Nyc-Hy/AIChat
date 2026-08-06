# Codex Desktop 需 user 真机截图清单

> **生成时间**：2026-08-01（C 阶段 r0.4）
> **生成方式**：subagent 跑 16 次 web_search + 1 次 web_fetch 仍不能定调，必须 user 真机验证
> **保存位置**：截图请放 `docs/competitor-evidence/screenshots/`，命名 `2026-08-XX-codex-<scenario>.png`

---

## 总览

C 阶段 subagent 把 7 个 `not-found` 项研究完后，4 项**必须** user 真机截图才能定调（其余 3 项已在 PARITY_TRACKING.md 标 `deferred` + **AIChat 自创** tag）：

| ID | 场景 | 截图位置（Codex Desktop 哪个面板） | 期望结论 | 决定后续 |
|---|---|---|---|---|
| `NAV-NEW-03` | 在 "Recent" 段右键一个 chat | sidebar `Recent` 段任一 chat → 右键菜单 | **有** "Add to / Move to project" → 升级 `screenshot-confirmed`；**无** → 维持 `deferred` + AIChat 自创 | Wave 3 |
| `NAV-SCHED-03` | 找一个已存在的 scheduled task | sidebar `已安排` → 任一 task → 三点菜单 / row action | **有** "Run now" / "Trigger" → 升级 `screenshot-confirmed`；**无** → 维持 `deferred` + AIChat 自创 | Wave 9 |
| `PLG-UPGRADE-01` | 找一个已装插件的详情页 | sidebar `插件` → 已装插件 → 详情/设置 | **有** "Update" / "New version available" → 升级 `screenshot-confirmed`；**无** → 维持 `deferred` + AIChat 自创 | Wave 8 |
| `ENV-SUBAGENT-FAILED-01` | 触发一个故意失败的 subagent | Environment 面板 `子智能体` 段（看是否有 Failed 标签/分组） | **有** "Failed" 标签 → 升级 `screenshot-confirmed`；**无** → 维持 `deferred` + AIChat 自创 | Wave 7 |
| `ENV-STANDALONE-01` | 不选 project，新建 chat | 右侧 Environment 面板（看 Standalone 状态下显示哪些 section） | 截图决定 Standalone 隐藏 / 简化 / 全显示哪些 | Wave 5 |

---

## 每项操作步骤

### 1. `NAV-NEW-03` — chat 右键菜单

**Codex 操作**：
1. 启动 Codex Desktop
2. 在 sidebar `Recent` 段找到任一已有 chat（不是 `项目` 段的项目，是 `Recent` 段的独立 chat）
3. **右键**该 chat
4. 截图菜单内容

**保存命名**：`2026-08-XX-codex-chat-rightclick.png`

**判定**：
- 有 "Add to project" / "Move to project" / "Convert to project chat" 等项 → 截图证实 → 升级 `screenshot-confirmed` + Wave 3 直接对等
- **只有** "Open" / "Delete" / "Rename" 等基础项 → 截图证实无 move 入口 → 维持 `deferred` + AIChat 自创
- 完全没右键菜单 → 截图证实 chat 无 affordance → `inferred` 标注"chat 不可移动"

**耗时**：~2 min

### 2. `NAV-SCHED-03` — Scheduled "Run now"

**Codex 操作**：
1. 启动 Codex Desktop
2. 在 sidebar `已安排` 段找任一已存在的 scheduled task（如果一个都没有，先随便建一个 1 分钟后跑的 task，1 分钟后它就会出现在列表）
3. **点击**该 task 进入详情
4. 看详情页有没有 "Run now" / "Trigger now" / "立即运行" 按钮
5. 截图（如果没按钮，截详情页全貌证明 "无 Run now"）

**保存命名**：`2026-08-XX-codex-scheduled-detail.png`

**判定**：
- 有 Run now 按钮 → subagent 的全文档 fetch 漏了，升级 `screenshot-confirmed`
- 无 Run now 按钮（详情页只有 pause / archive / history）→ 维持 `deferred` + **AIChat 自创**

**耗时**：~5 min（含建一个 scheduled task 等 1 分钟）

### 3. `PLG-UPGRADE-01` — Plugin in-place 升级

**Codex 操作**：
1. 启动 Codex Desktop
2. 在 sidebar `插件` 段打开任一**已装**插件
3. 看插件详情页有没有 "Update" / "New version available" / "升级" 按钮
4. 截图

**保存命名**：`2026-08-XX-codex-plugin-detail.png`

**判定**：
- 有 Update 按钮 → 升级 `screenshot-confirmed`（subagent 漏了）
- 无 Update 按钮（只有 "Remove" / "Disable"）→ 维持 `deferred` + **AIChat 自创**

**耗时**：~2 min

### 4. `ENV-SUBAGENT-FAILED-01` — Subagent Failed 分组

**Codex 操作**（关键步骤）：
1. 启动 Codex Desktop
2. 选一个项目（不是 Standalone chat）
3. 触发一个**故意失败**的 subagent：
   - 方案 A：在项目 `AGENTS.md` 写一个会失败的正则表达式，让 subagent 去 grep → 必然报错
   - 方案 B：让 main agent dispatch 一个 invalid task 给 subagent
   - 方案 C：跑一个肯定不通的 shell command（subagent 通常会调 tool）
4. 等 subagent 失败后，看右侧 Environment 面板 `子智能体` 段
5. 截图

**保存命名**：`2026-08-XX-codex-subagent-failed.png`

**判定**：
- 有 `Failed` 标签 / 分组 → 升级 `screenshot-confirmed`
- 只有 `66 完成` + 4 icon (r0.3 截图状态) → 维持 `deferred` + **AIChat 自创**

**耗时**：~5-10 min（要等 subagent 跑完失败）

### 5. `ENV-STANDALONE-01` — Standalone Session 状态

**Codex 操作**：
1. 启动 Codex Desktop
2. **不选**任何项目
3. 点击 `新对话` 创建一个 Standalone chat
4. 截图右侧 Environment 面板
5. （可选）再切到有 project 的 chat，对比截图

**保存命名**：`2026-08-XX-codex-standalone-session.png`

**判定**：
- Standalone 下 Environment 完全隐藏 / 简化 → 升级 `screenshot-confirmed`
- Standalone 下 Environment 仍显示某些 section → 标"Standalone 隐哪些 sections"具体清单
- 关键问题：Standalone 下 "本地" section（branch selector + commit）应该不可见（无 git repo），"来源" section 应该仍可见（可粘贴文本/网页）

**耗时**：~2 min

---

## 怎么把截图给我

**最方便**：
1. 截图保存到 `docs/competitor-evidence/screenshots/2026-08-XX-codex-<scenario>.png`
2. 在下一次 session 告诉我"截图放在 docs/competitor-evidence/screenshots/"
3. 我会读图 + 升级 PARITY_TRACKING.md 对应行

**如果你赶时间 / 不想自己跑**：
- 告诉我"我帮你跑 Computer Use" → 我用 `playwright` skill + macOS Codex app 自动化
- 但我之前 memory 记过 `screencapture -x` 截的是整屏不是单窗，你直接传图更准

---

## 跑完后 PARITY_TRACKING.md 预期变化

| ID | 当前 | 跑完后可能 |
|---|---|---|
| `NAV-NEW-03` | `deferred` + `screenshot-required` | `screenshot-confirmed` (有菜单) / 维持 `deferred` (无菜单) |
| `NAV-SCHED-03` | `deferred` (AIChat 自创) | `screenshot-confirmed` (有按钮，subagent 漏) / 维持 |
| `PLG-UPGRADE-01` | `deferred` + `screenshot-required` | `screenshot-confirmed` / 维持 |
| `ENV-SUBAGENT-FAILED-01` | `deferred` + `screenshot-required` | `screenshot-confirmed` / 维持 |
| `ENV-STANDALONE-01` | `inferred` + `screenshot-required` | `screenshot-confirmed` (有明确状态) / 维持 `inferred` |

跑完后 Wave 0 evidence 收官（**0 个 inferred / 0 个 not-found**），可以正式进入 Wave 1 schema 迁移。
