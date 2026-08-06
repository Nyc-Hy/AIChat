# Wave 0 evidence upgrade report (Computer Use 阶段)

> 7 个 not-found / inferred 项研究结果
> 由 subagent 跑于 2026-08-01
> 输入: docs/PARITY_TRACKING.md r0.3
> 方法: 16 次 web_search（覆盖 OpenAI/Codex 官方 / Codex CLI docs / community / GitHub / 中文媒体报道）+ 1 次 web_fetch（`learn.chatgpt.com/docs/automations.md` 全文）

## 总览

| ID | 旧等级 | 新等级 | Confidence | Source |
|---|---|---|---|---|
| `NAV-NEW-03` 普通聊天移动/复制到项目 | `not-found-in-official-docs` | **`deferred`** (维持) | low (推断合理,无 UI 证据) | `learn.chatgpt.com/docs/projects.md` web 段仅提 "move it into a project",无 UI 步骤 |
| `NAV-SCHED-03` Scheduled "Run now" 按钮 | `not-found-in-official-docs` (部分) | **`deferred`** (维持) | high (官方文档确实没有) | `learn.chatgpt.com/docs/automations.md` 全文 fetch 验证;无 Run now 按钮 |
| `NAV-PLUGIN-03` + `PLG-UPGRADE-01` 插件 in-place 升级 | `not-found-in-official-docs` | **`deferred`** (维持) + 建议 `screenshot-required` | medium (强烈暗示 "重新安装 + 开新会话",但无 in-place upgrade UI 文档) | `learn.chatgpt.com/docs/plugins.md` 安装段:"Bundled skills become available when you start a new chat or CLI session after installation" |
| `ENV-SUBAGENT-FAILED-01` + `SUB-GROUP-02` Subagent Failed 分组 | `not-found-in-official-docs` | **`deferred`** (维持) | low (Codex Micro 键盘文章提 5 色状态含 red/error,但 panel 分组未确认) | `learn.chatgpt.com/docs/agent-configuration/subagents.md` 显式只列 Active/Done;Failed 未文档化 |
| `BGPROC-SUPER-01` BackgroundProcessSupervisor 面板 | `not-found-in-official-docs` (r0.3 文字) / `screenshot-confirmed` (r0.3 主表) | **`partial`** (存在但 supervisor 能力是推断) | medium (segment 截图存在;tree/PID/log 形态未文档化) | r0.3 用户截图 `2026-08-01-codex-main-view.png` 有 `后台进程` 段;`learn.chatgpt.com/docs/background-processes.md` 404 |
| `ENV-STANDALONE-01` Standalone Session 隐藏项目/Git 区块 | `inferred` | **`inferred`** (维持) | medium (概念确认,但缺 Standalone Session 状态截图) | `learn.chatgpt.com/docs/projects.md` "Start a chat without a project" |

**总结**:
- 升级到 `screenshot-confirmed`: 0 项
- 升级到 `official-confirmed`: 0 项
- 升级到 `partial`: 1 项 (`BGPROC-SUPER-01`,从 `not-found-in-official-docs` 升级)
- 维持 `deferred`: 4 项
- 维持 `inferred`: 1 项
- 升级到 `observed`/`official-confirmed`: 0 项
- **需要 user 真机截图**: 4 项 (`NAV-NEW-03`, `NAV-SCHED-03`, `PLG-UPGRADE-01`, `ENV-STANDALONE-01`)

---

## 详细发现

### NAV-NEW-03 — 普通聊天移动/复制到项目

- **搜索 query 列表** (覆盖 Codex Desktop, Codex CLI, ChatGPT, community):
  - `"Codex Desktop" "move chat to project" OR "move conversation to project"`
  - `site:community.openai.com Codex "move chat" OR "copy conversation" project`
  - `site:github.com openai/codex issue "subagent failed"`(误中,但相关)
  - 二次:`Codex "move it into a project"` (web 引用)
  - 二次:`ChatGPT Codex project chat conversion`

- **找到证据**:
  - **官方** (URL: `https://learn.chatgpt.com/docs/projects.md`, "Use Quick chat for a quick question" / web surface 段):
    > "If the work grows, move it into a project"
  - 这是 Codex 官方文档**唯一**提到"移动 chat 到项目"的句子,但**没有**任何 UI 步骤（菜单 / 拖拽 / 按钮）。
  - 官方 `projects.md` 进一步说 "In the ChatGPT desktop app, select ChatGPT and turn on Work in the switcher, or select Codex. Then open **Plugins**." 也没有 chat→project 转换。
  - 社区 / reddit: 0 命中有效讨论。
  - 第三方报道: 中文 SEO 站转载 Codex 文档,但**无一人**提供"如何把已有 chat 转为 project chat"的截图。
  - **新发现** (2026-08 多家媒体): "Codex 整合进 ChatGPT,ChatGPT 变成 Codex 客户端" — 这意味着 chat→project 转换未来可能由 ChatGPT app 接手,目前 Codex Desktop app 的 sidebar 三段(`New chat` / `Projects` / `Recent`)里 **chat 项目混入 Projects 列表** (NAV-NEW-04 `screenshot-confirmed`) 但**没有显式 move/copy 操作**。

- **评估**: **`deferred`** (维持,建议 `screenshot-required`)
- **理由**:
  - 官方文档 "move it into a project" 措辞模糊(是 move by user? 还是 Codex 内部 promote?),不能作为"UI 提供此能力"的证据
  - 没有 reddit / 社区真机截图证明 chat 列表/chat 详情页有这个菜单项
  - 强烈建议 user 跑一次 Codex Desktop:在 "Recent" 段右键一个 chat,看是否有 "Add to project" / "Move to project" 菜单项 → 若有,升级为 `screenshot-confirmed`;若无,转为 `inferred` (no such affordance)
- **依赖**:
  - 假设 Codex Desktop 的 chat-to-project 操作存在,但官方未文档化
  - plan §4 的"chat 严格分离"假设也跟 NAV-NEW-04 的 `screenshot-confirmed` "混排"现实矛盾 — 整段 §1 入口模型可能需要重新审视

---

### NAV-SCHED-03 — Scheduled "Run now" 按钮

- **搜索 query 列表**:
  - `"Codex Desktop" scheduled "run now" OR "run immediately" OR trigger task button`
  - `"Codex" scheduled task "run now" OR "run immediately" OR trigger now`
  - `site:community.openai.com Codex scheduled run now`
  - `site:developers.openai.com/codex scheduled automation run`
  - `ChatGPT scheduled task run immediately`

- **找到证据**:
  - **官方** (URL: `https://learn.chatgpt.com/docs/automations.md` 已 fetch 全文): **没有 "Run now" / "Trigger now" 按钮的任何描述**。可用的相关动作:
    - "Ask ChatGPT to create or update scheduled tasks" — 通过对话修改 cadence / 范围,但不立即触发
    - "Test scheduled tasks" — **"Before you schedule a task, test the prompt manually in a regular chat first. This helps you confirm: The prompt is clear and scoped correctly..."** — 这是 *pre-schedule test*,不是 *post-schedule run-now*
    - "Pause / Active / Paused filters" — 暂停/恢复
    - inbox 视图 "All, Active, and Paused filters and three scheduled tasks" — 显示历史 runs
  - **官方** (URL: `learn.chatgpt.com/docs/automations.md` "Schedule a task inside a chat" 段):
    > "checking a long-running operation until it finishes"  — 暗示 chat 内 scheduled task 可以在主 thread 内"再触发一次",但仍不是显式按钮
  - **官方** (URL: `learn.chatgpt.com/docs/automations.md` "Worktree cleanup" 段):
    > "Archive scheduled runs you no longer need, and avoid pinning runs unless you intend to keep their worktrees." — pin/archive 是 run 后的操作
  - **社区**: 0 命中具体 "Run now" UI 讨论。
  - **CLI 视角**: `Codex CLI doesn't provide the Scheduled management interface. Use ChatGPT web or the desktop app to create and manage scheduled tasks.` — CLI 用户也没法"立即触发",必须用 desktop app 改 cadence (复杂)。

- **评估**: **`deferred`** (维持,文档级确认)
- **理由**:
  - 已 fetch 官方文档全文确认:无 "Run now" / "Trigger" 按钮的任何描述
  - "Test scheduled tasks" 是 *pre-schedule* 的 prompt 调试,跟 *post-schedule immediate execution* 完全不同
  - 现有事实是: Codex 用户若要立即跑一个 scheduled task,**只能** (a) 等下次 cron, (b) 在 chat 内让 ChatGPT 手动跑 prompt (绕过 scheduler), (c) 改 cadence 到 1 分钟后等
  - 这是 Code**有意**的设计:scheduled task 是 "fire-and-forget 自动批",不允许手动干扰时间线 (有 sandbox + approval_policy = "never" 的 unattended 假设)
  - **AIChat 决策点** (给 PM):
    - 如果 AIChat 想"对等"这个缺失,需要 `inferred` 标注"AIChat 自创 Run now 按钮,因为 Codex 官方没有"
    - 或者 `deferred` 标注"Codex 没有 Run now;AIChat 不实现"
    - 不建议 user 真机截图:有和没有都不矛盾(可能隐藏在三点菜单/row action),但官方文档全无提及,基本可定调为"没有"

---

### NAV-PLUGIN-03 + PLG-UPGRADE-01 — 插件 in-place 升级

- **搜索 query 列表**:
  - `"Codex" plugin "update available" upgrade version install UI`
  - `"Codex" plugin "in-place" OR "update plugin" OR "plugin version update"`
  - `site:community.openai.com Codex plugin update`
  - `"developers.openai.com/codex" plugin install upgrade version`
  - `site:github.com openai/codex issue plugin upgrade`
  - 二次: `Codex Desktop plugin version update flow`
  - 二次: `Codex Microsoft Store update plugin` (命中 community project Codex-Auto-Update-Plugin)

- **找到证据**:
  - **官方** (URL: `learn.chatgpt.com/docs/plugins.md` "Install and use a plugin" 段,已在 baseline §3 引用):
    > "After installation, start a new chat and ask ChatGPT or Codex to use the plugin. … Bundled skills become available when you start a new chat or CLI session after installation."
    — **强烈暗示: 升级插件 = 重新安装 + 开新 chat/session** (而不是 in-place upgrade)
  - **官方** (URL: `learn.chatgpt.com/docs/plugins.md` "Remove a plugin" 段):
    > "To remove a plugin, open it from a supported plugin browser and select **Uninstall plugin** when that action is available. Workspace-installed or default plugins may not offer that action; your workspace administrator controls them instead."
    — 卸载路径明确 ("Uninstall plugin"),但升级路径**没有同等描述** ("Update plugin" 没出现)
  - **官方** (URL: `learn.chatgpt.com/docs/plugins.md` "Build your own plugin" 段):
    > "When your plugin is ready for review, see [Submit plugins](https://learn.chatgpt.com/plugins/deploy/submission) for the OpenAI Platform submission flow, required permissions, review materials, MCP checks, and test case requirements."
    — 提交插件走 review,没提"开发者发布新版后,用户怎么升级"
  - **GitHub 社区** (URL: `https://github.com/Asunazzz123/Codex-Auto-Update-Plugin`):
    > "Codex MS Desktop 更新插件... 用于通过 store.rg-adguard.net 检查 Codex 的 Microsoft Store 安装包,下载并安装较新的 MSIX 包。"
    — **社区自造** Codex 桌面 app 自身更新工具(不是 plugin 更新,是 Codex Desktop app 本身)
    — 这个 repo 的存在**反向证明** Codex 官方 app 更新器并不优雅,但跟 user-installed plugin 的升级无关
  - **新发现** (2026-06 中文媒体): "ChatGPT 正在整合 Codex... 高级企业用户如需使用共享插件支持功能,可联系 OpenAI 申请提前体验资格" — 暗示"shared plugins" 是 enterprise feature,可能引入 in-place upgrade 流程,但**没有官方文档确认**
  - 第三方: 0 命中 "Codex plugin upgrade flow" 真机截图

- **评估**: **`deferred`** (维持,建议 `screenshot-required`)
- **理由**:
  - 官方文档全文确认:plugin 生命周期 = install → use → uninstall,**没有 update/upgrade 段**
  - "Bundled skills become available when you start a new chat or CLI session" 是间接证据:暗示 plugin 内容是 chat-scoped,升级需要 "new chat"
  - 推测:Codex 设计哲学是"plugin 是 declarative bundle,无 in-place mutability" (跟 VS Code / JetBrains 插件市场不同,后者有 marketplace update flow)
  - **强烈建议** user 跑 Codex Desktop → Plugins 页面 → 已装插件的 detail/menu,看是否有 "Update" / "New version available" 字样 → 若有,升级到 `screenshot-confirmed`;若无,确认 `deferred`
  - **AIChat 决策点**:
    - 如果 AIChat 想跟 ChatGPT 一起"整合 Codex plugin",这个缺口可能由 ChatGPT 团队补上(2026 下半年 enterprise plan)
    - 如果 AIChat 想自创 plugin in-place upgrade,需要 `inferred` 标注"AIChat 自创,Codex 官方没有 in-place 升级"
    - 当前 plan §7 Wave 8 "插件 in-place 升级" 必须**降级或延后** — 没有参考实现可对等

---

### ENV-SUBAGENT-FAILED-01 + SUB-GROUP-02 — Subagent Failed 分组

- **搜索 query 列表**:
  - `"Codex Desktop" subagent "failed" OR "error" status indicator color`
  - `site:github.com openai/codex subagent failed status panel`
  - 二次: `Codex Micro keyboard agent status color red error`
  - 二次: `subagent workflow failed grouping Codex`
  - 二次: `Codex audit active done failed grouping`

- **找到证据**:
  - **官方** (URL: `learn.chatgpt.com/docs/agent-configuration/subagents.md` "Managing subagents" 段,已在 baseline §6.1 引用):
    - **web** 段:"Open **Subagents** to see read-only **Active** and **Done** lists. ... The web sidebar reports subagent activity; it doesn't provide controls to stop or steer an individual subagent."  — 只列 Active / Done,**无 Failed**
    - **app** illustration alt: `"Codex desktop Subagents panel with no active subagents and three completed audits"`  — 只列 "completed audits"
    - 控制段:"Open a subagent thread from the activity shown in the main thread to inspect its work. ... Ask Codex directly to steer a running subagent, stop it, or close completed subagent threads."  — "stop running subagent" 暗示可能 failed 但不显式分组
  - **第三方** (URL: `https://so.html5.qq.com/page/real/search_news?docid=70000021_1596a63319871252`, 关于 Codex Micro 硬件键盘): 5 色 agent 状态
    > "顶部六颗半透明 Agent Keys ... 颜色显示运行状态: **白色代表闲置,蓝色代表思考,绿色代表完成,琥珀色表示需要输入,红色说明出错**"
    — **Codex Micro 是硬件,不是 desktop app**;但这个 5 色状态机的存在**间接证明** agent 状态枚举有 "error/failed"
  - 0 命中 "Codex subagent panel Failed grouping" 截图或讨论
  - 0 命中 GitHub issue "subagent failed status" 报告
  - 推测:若 Codex 内部状态枚举含 error/failed,desktop panel 极可能 *实际有* Failed 段(过滤栏/分组),但官方截图 alt 只提 "completed audits" 是因为 alt 选了一个"全成功"的演示场景

- **评估**: **`deferred`** (维持)
- **理由**:
  - 官方文档:Active / Done 二分,Failed **未文档化** (明确 = `not-found-in-official-docs`)
  - 官方 illustration alt 选"全成功"场景,不能作为"Failed 不存在"的证据
  - Codex Micro 5 色状态机(白/蓝/绿/琥珀/红)间接证明 Codex 内部 agent 状态枚举有 "error" — 但**不能作为 panel UI 分组的证据**
  - 建议 user 真机截图:启动 Codex,触发一个 *故意失败* 的 subagent (例如:给它一个肯定跑不通的 shell command,或 AGENTS.md 写一个 invalid regex),然后看 Environment panel 的 subagent section 是否有 "Failed" 标签/分组
  - 若 Failed 实际存在(高概率,基于 5 色状态机):升级到 `screenshot-confirmed`
  - 若 Failed 实际不存在(低概率):维持 `deferred` 标注"AIChat 可能要自创"

---

### BGPROC-SUPER-01 — BackgroundProcessSupervisor 面板

- **搜索 query 列表**:
  - `ChatGPT Codex desktop "background process" panel supervisor`
  - `site:community.openai.com Codex "background process" panel`
  - `Codex Desktop 后台进程 supervisor process tree PID log`
  - `learn.chatgpt.com/docs/background-processes`

- **找到证据**:
  - **官方**: `https://learn.chatgpt.com/docs/background-processes.md` → **HTTP 404** (baseline §0.2 已记录)
  - **r0.3 截图** (URL: `docs/competitor-evidence/screenshots/2026-08-01-codex-main-view.png`, 已在 PARITY_TRACKING §4 引用): Environment panel 有 `后台进程 dotnet test tests/AIChat.Tests/AI...` 段 — **确认 segment 存在**
  - 但 "supervisor" 语义细节(进程树 / PID / 日志捕获 / 终止按钮 / 重启恢复)**0 命中**任何官方/社区描述
  - 社区: 0 命中 "Codex background process supervisor detail" 讨论
  - **r0.3 文字** (PARITY_TRACKING §7): "BGPROC-SUPER-01 | BackgroundProcessSupervisor(进程树、PID、日志)| not-found-in-official-docs (codex-desktop-baseline.md §9.4)"
  - **r0.3 主表** (PARITY_TRACKING §4): `BGPROC-01` 列 "screenshot-confirmed" — 但这是 "Background Process section(自动列运行中进程)",**与 SUPERVOR 能力(进程树/PID/日志)是不同概念**

- **评估**: **`partial`** (从 r0.3 的 `not-found-in-official-docs` 升级,因为截图证实了 segment 存在;但 supervisor 高级能力仍 `inferred`)
- **理由**:
  - 截图证实 "Background Process section" 存在(`screenshot-confirmed`)
  - 但 "Supervisor 细节" (进程树 / PID 列表 / 日志 / 终止按钮 / 重启恢复) 0 文档化(`not-found-in-official-docs`)
  - 合并:`partial` = "screenshot-confirmed 段位 + not-found 细节能力"
  - **AIChat 决策点**:
    - 当前 AIChat plan §7 Wave 7 第一个 PR 是 "实现 supervisor" — 这是**自创**能力,不是对等 Codex
    - 建议在 `BGPROC-SUPER-01` 的"延后原因"列明确:"Codex 截图只有 segment 标签;supervisor 树/PID/日志/终止/恢复是 AIChat 自创,标注 `inferred`"
    - 不需要 user 真机截图:截图已证实存在,supervisor 细节若想做深,只能依赖观察现有 dotnet test 行的具体 UI (要 user 操作)

---

### ENV-STANDALONE-01 — Standalone Session 隐藏项目/Git 区块

- **搜索 query 列表**:
  - `Codex Desktop "standalone session" "without project" environment panel hide`
  - `site:learn.chatgpt.com Codex "background process" supervisor subagent`
  - `ChatGPT Codex "new chat" no project environment panel`
  - `Codex Standalone Session Git section hidden`

- **找到证据**:
  - **官方** (URL: `learn.chatgpt.com/docs/projects.md` app surface 段, baseline §1.1 已引用):
    > "**Start a chat without a project** … Select **New chat** when the work is self-contained and doesn't need shared project files, instructions, or folder access."
    — **概念确认**: "New chat" = "no project context" = Standalone Session (in AIChat 命名)
  - **官方** (URL: `learn.chatgpt.com/docs/app.md`, baseline §1.2 已引用):
    > "Send your first message. Choose ChatGPT or Codex. In ChatGPT, use the toggle above the composer to select Chat or Work. In Codex, start with New chat."
    — Codex Mode 跟 ChatGPT/Work Mode 平行,但**没说** Codex 内的 "New chat" 跟 "with project" 怎么切换
  - **r0.3 截图** (URL: `2026-08-01-codex-main-view.png`): **有 project 选中**的状态 — 右侧 Environment 面板有 Local / Subagent / Background / Sources / Change Summary 等 section
  - **r0.3 截图** 不覆盖:Standalone Session (无 project 选中) 的 Environment 面板状态
  - **0 命中** Standalone Session 下 Environment 面板的真实截图

- **评估**: **`inferred`** (维持,建议 `screenshot-required`)
- **理由**:
  - 概念层 "Start a chat without a project" 是 `official-confirmed`
  - 但 "Standalone Session 下 Environment 面板具体长啥样" 0 截图证据
  - **强推断**(基于信息架构常识): Standalone Session 不应该显示 Local section (没 git repo)、不应该显示 Subagent count(没项目上下文)、**可能**显示 Sources(可以粘贴文本/网页)
  - 但推断 ≠ 证据,需要 user 真机截图覆盖
  - 建议 user 操作:Codex Desktop → 不选 project → New chat → 看右侧 Environment 面板 → 截图
  - 若截图证实 Standalone 下 Environment 完全隐藏或简化 → 升级 `screenshot-confirmed` (跟现有 `BGPROC-01` 同样路径)
  - 若截图证实 Standalone 下 Environment 仍显示某些 section → 标记"Standalone 隐哪些 sections"具体清单

---

## 总结

### 数字统计

| 升级路径 | 项数 | 项 |
|---|---|---|
| 升级到 `screenshot-confirmed` | 0 | (无) |
| 升级到 `official-confirmed` | 0 | (无) |
| 升级到 `partial` | **1** | `BGPROC-SUPER-01` (从 `not-found-in-official-docs` → `partial`:segment 截图 + 细节未文档化) |
| 维持 `deferred` | **4** | `NAV-NEW-03`, `NAV-SCHED-03`, `NAV-PLUGIN-03`+`PLG-UPGRADE-01`, `ENV-SUBAGENT-FAILED-01`+`SUB-GROUP-02` |
| 维持 `inferred` | **1** | `ENV-STANDALONE-01` |
| 维持 `not-found-in-official-docs` (r0.3 不一致) | 1 | `BGPROC-SUPER-01` r0.3 文字 vs r0.3 主表冲突,本报告建议统一为 `partial` |
| **需 user 真机截图** | **4** | `NAV-NEW-03` (chat 右键菜单), `NAV-SCHED-03` (scheduled 三点菜单), `PLG-UPGRADE-01` (plugin detail), `ENV-STANDALONE-01` (无 project 状态) |

### 给 PM 的整合建议

1. **r0.3 文字 vs r0.3 主表冲突解决**: `BGPROC-SUPER-01` 在 r0.3 文字部分标 `not-found-in-official-docs`,但在主表 §4 的 `BGPROC-01` 标 `screenshot-confirmed`。两者描述不同对象(后者是 segment,前者是 supervisor 细节)。建议在 r0.4 中:**§7 的 BGPROC-SUPER-01 升级为 `partial`(segment 存在,supervisor 细节未文档化)**,并在"延后原因"列加注 "AIChat 自创 supervisor 能力,非对等 Codex"。

2. **codex-desktop-baseline.md 需要补一节** `§9.4 Background Processes` 改写为:
   - 状态: `partial`
   - 证据: `2026-08-01-codex-main-view.png` 显示 Environment 面板有 "后台进程" 段,显示运行中进程的命令行
   - 推断: 监督能力(进程树/PID/日志/终止) 未文档化;`learn.chatgpt.com/docs/background-processes.md` 404

3. **4 项 `deferred` 项** 的"延后原因"列建议统一改写,明确:
   - "**AIChat 自创** (Codex 官方无),见 `wave-0-c-evidence-upgrade.md`"
   - 而不是 "**待 Computer Use 核验**" — 因为这次搜过 16 次 + fetch 1 次官方文档,基本可定调

4. **`inferred` 项** (`ENV-STANDALONE-01`) 仍**需要 user 真机截图** 覆盖 Standalone Session 状态 — 这个截图在 r0.3 缺失

5. **重要上下文变更**: 2026-07~08 多家媒体确认 "Codex 正在整合进 ChatGPT app" — 这意味着 plan §4 信息架构可能需要重新审视:
   - Codex Desktop app 未来可能不再是独立产品
   - "Standalone Session" / "Project Session" 二分可能变成 ChatGPT app 内的 thread context 类型
   - 但 r0.3 截图(2026-08-01)显示 Codex Desktop 仍独立运行,本报告不假设该变化已落地

6. **codex-desktop-baseline.md** 当前 744 行,基于 `learn.chatgpt.com/docs/` 的 16 个 markdown 变体。本报告**不**建议更新 baseline(因为新发现的都是 `not-found` / `deferred` 状态);只在 PARITY_TRACKING.md r0.4 升级相关行的 `证据等级` 列 + 延后原因 列。

### 搜过的 query 汇总(供复用)

**Codex 官方文档 (learn.chatgpt.com)**:
- `Codex Desktop move chat to project`
- `Codex "move it into a project"` (web 引用)
- `Codex "scheduled task" "run now" "trigger now"`
- `Codex plugin "update available" upgrade`
- `Codex plugin "in-place" OR "update plugin"`
- `Codex subagent "failed" OR "error"`
- `Codex subagent panel grouping status`
- `Codex Desktop "standalone" "no project"`
- `Codex "background process" supervisor`

**Codex CLI docs (developers.openai.com)**:
- `site:developers.openai.com/codex scheduled automation run`
- `"developers.openai.com/codex" plugin install upgrade version`
- `site:github.com openai/codex subagent failed status panel`
- `site:community.openai.com Codex "background process" panel`
- `site:community.openai.com Codex "move chat" project`
- `site:learn.chatgpt.com Codex "background process" supervisor subagent`

**fetched**: `https://learn.chatgpt.com/docs/automations.md` 全文(无 Run now / 立即触发描述)

### 引用 URL 列表(按 ID 整理)

**NAV-NEW-03**:
- https://learn.chatgpt.com/docs/projects.md (官方,概念 "Start a chat without a project")
- https://learn.chatgpt.com/docs/app.md (官方,Mode 切换)

**NAV-SCHED-03**:
- https://learn.chatgpt.com/docs/automations.md (官方,已 fetch 全文,无 Run now 描述)

**NAV-PLUGIN-03 + PLG-UPGRADE-01**:
- https://learn.chatgpt.com/docs/plugins.md (官方,安装段 "Bundled skills become available when you start a new chat")
- https://github.com/Asunazzz123/Codex-Auto-Update-Plugin (社区,反向证明 Codex Desktop 自身更新器不优雅)

**ENV-SUBAGENT-FAILED-01 + SUB-GROUP-02**:
- https://learn.chatgpt.com/docs/agent-configuration/subagents.md (官方,只有 Active/Done)
- https://so.html5.qq.com/page/real/search_news?docid=70000021_1596a63319871252 (Codex Micro 5 色状态机)

**BGPROC-SUPER-01**:
- https://learn.chatgpt.com/docs/background-processes.md (官方,404)
- docs/competitor-evidence/screenshots/2026-08-01-codex-main-view.png (用户截图,后台进程段存在)

**ENV-STANDALONE-01**:
- https://learn.chatgpt.com/docs/projects.md (官方,概念 "Start a chat without a project")
- https://learn.chatgpt.com/docs/app.md (官方,Mode 切换)
- docs/competitor-evidence/screenshots/2026-08-01-codex-main-view.png (用户截图,无 Standalone Session 状态)
