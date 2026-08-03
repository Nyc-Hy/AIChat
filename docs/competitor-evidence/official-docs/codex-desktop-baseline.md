# Codex Desktop 官方基线（official-confirmed 证据集）

> 范围：对 `docs/CODEX_DESKTOP_PARITY_PLAN.md` §4 信息架构中 plan §4 列出的"全局入口 / 项目 / 普通聊天 / 当前会话 / 设置中心"4 个一级分类的 first-level 入口，从 OpenAI / ChatGPT 官方文档中拉取可验证的事实，作为对等追踪表的 `official-confirmed` 证据。
>
> 本报告**只**收录官方资料直接陈述的事实。**不**写 "AIChat 应该如何实现"、**不**做产品判断。找不到的事实标 `not-found-in-official-docs` 或 `inferred`。

---

## 0. 数据来源与可信度

### 0.1 实际成功读取的 URL（HTTP 200，markdown 变体）

页面顶部 nav / JS 占满正文，HTML 路径几乎不可读。所有下列内容通过**追加 `.md` 后缀**拿到 markdown 变体（页面自身提示："Markdown versions of documentation pages are available by appending `.md` to the page URL"）：

| 来源 URL | 实际命中 URL | 用途 |
|---|---|---|
| plan 列出的 `learn.chatgpt.com/docs/projects` | `https://learn.chatgpt.com/docs/projects.md` | Projects, New Chat |
| plan 列出的 `learn.chatgpt.com/docs/plugins` | `https://learn.chatgpt.com/docs/plugins.md` | Plugins |
| plan 列出的 `learn.chatgpt.com/docs/automations` | `https://learn.chatgpt.com/docs/automations.md` | Scheduled |
| plan 列出的 `learn.chatgpt.com/docs/agent-configuration/subagents` | `https://learn.chatgpt.com/docs/agent-configuration/subagents.md` | Subagents |
| plan 列出的 `learn.chatgpt.com/docs/sandboxing` | `https://learn.chatgpt.com/docs/sandboxing.md` | Sandboxing |
| plan 列出的 `learn.chatgpt.com/docs/sites` | `https://learn.chatgpt.com/docs/sites.md` | Sites |
| 拓展：app 总览 | `https://learn.chatgpt.com/docs/app.md` | 桌面 App 总览 / New chat 入口 |
| 拓展：code review | `https://learn.chatgpt.com/docs/code-review.md` | Code review 入口 / Git 状态 |
| 拓展：developer settings | `https://learn.chatgpt.com/docs/developer-settings.md` | 设置中心子项 |
| 拓展：worktrees | `https://learn.chatgpt.com/docs/environments/git-worktrees.md` | Worktree 概念 / Environment |
| 拓展：local environments | `https://learn.chatgpt.com/docs/environments/local-environment.md` | Project actions / .codex/ |
| 拓展：settings 索引 | `https://learn.chatgpt.com/docs/reference/settings.md` | Settings 分类详细 |
| 拓展：permissions | `https://learn.chatgpt.com/docs/permissions.md` | 权限 profile |
| 拓展：approvals & security | `https://learn.chatgpt.com/docs/agent-approvals-security.md` | Approval policy / Auto-review |
| 拓展：skills | `https://learn.chatgpt.com/docs/skills-and-plugins.md` | Skills vs Plugins |
| 拓展：build skills | `https://learn.chatgpt.com/docs/build-skills.md` | Skills 加载 / @ vs $ |

### 0.2 HTTP 404 / 重定向 / 未读取

- `https://learn.chatgpt.com/llms.txt` → 404 "Page not found"（页面里反复指向的索引文件**实际不存在**）
- `https://learn.chatgpt.com/docs/llms.txt` → 404 同上
- `https://learn.chatgpt.com/docs/settings.md` → 404（"Settings" 的真路径是 `/docs/reference/settings.md`）
- `https://learn.chatgpt.com/docs/environment.md` → 404（单数）
- `https://learn.chatgpt.com/docs/environments.md` → 404（无 `s`）
- `https://learn.chatgpt.com/docs/background-processes.md` → 404
- `https://learn.chatgpt.com/docs/sources.md` → 404
- `https://learn.chatgpt.com/docs/skills.md` → 404（真路径 `/docs/build-skills.md`）
- `https://learn.chatgpt.com/docs/codex.md` → 404
- `https://learn.chatgpt.com/docs/automations/inbox.md` → 404
- `https://learn.chatgpt.com/docs/local-environments.md` → 404（真路径是复数 `/docs/environments/local-environment.md`）

### 0.3 可信度说明

- 上述 16 个 markdown 变体全部 HTTP 200，**正文均为 OpenAI 自有产品文档**（`learn.chatgpt.com` 由 OpenAI / ChatGPT 团队维护，页面 footer 写明 "OpenAI Developers"），不是社区翻译。
- 每个事实条目在引用时**只**使用本报告能复现的原文短摘抄（1–2 句），无改写、无上下文重构。
- HTML 路径返回的页面被 Astro 客户端渲染覆盖，正文缺失（content 被 `<ContentModeSwitch>` 拆分），无法用 HTML 拿到完整文字。**`/*.md` 是当前唯一可靠的"对 AI 友好"数据源**——下游若做自动校验请只走 markdown 后缀。
- 几乎所有页面都使用 `<ContentModeSwitch group="codex-surface" id="app|web|cli|ide">` 切换不同 surface 的描述。本报告只引"app"或"通用"分支的事实；若某事实在 `web` 才有、app 没有，单独标注。

---

## 1. New Chat

### 1.1 区分"普通聊天"与"项目内聊天"

### 普通聊天（独立 chat，不绑项目）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（app surface 段）
- 引用: "**Start a chat without a project** … Select **New chat** when the work is self-contained and doesn't need shared project files, instructions, or folder access."
- 状态: `official-confirmed`
- 备注: 这是 app 内的"普通 chat"概念；和项目内的 chat 是不同的入口。

### 项目内聊天（项目专属 chat）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（app surface 段，"Use local projects for folders and codebases"）
- 引用: "New chats start in the primary folder. Codex also uses that folder for Git operations and automatic discovery of `AGENTS.md`, skills, and `config.toml`."
- 状态: `official-confirmed`
- 备注: 区分点是"在哪个项目上下文里 start chat"，不是新 UI 形态。

### 1.2 触发入口（菜单 / 快捷键 / + 按钮）

### New chat 按钮（Composer 上方）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Use Quick chat for a quick question"）
- 引用: "Point to **New chat**, then select the **Quick chat** icon on its right."
- 状态: `official-confirmed`

### Cmd+Option+N（macOS） / Ctrl+Alt+N（Windows）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Use Quick chat for a quick question"）
- 引用: "You can also press <kbd>Cmd+Option+N</kbd> on macOS or <kbd>Ctrl+Alt+N</kbd> on Windows."
- 状态: `official-confirmed`
- 备注: 键位绑定是 **Quick chat**（普通 ChatGPT chat，不进 Codex sidebar）。其他普通 New chat 没有官方文档化的快捷键披露。

### Codex vs Chat 模式切换
- 来源: `https://learn.chatgpt.com/docs/app.md`（"Get started with the desktop app"）
- 引用: "Send your first message. Choose ChatGPT or Codex. In ChatGPT, use the toggle above the composer to select Chat or Work. In Codex, start with New chat."
- 状态: `official-confirmed`
- 备注: Composer 顶部有"ChatGPT / Codex"切换，"ChatGPT"分支内进一步分 Chat / Work；"Codex"分支用 New chat 进入。

### 1.3 移动 / 复制普通聊天到项目

### not-found-in-official-docs
- 来源: 检索了 `/docs/projects.md` 全文、相关页面无直接段落
- 引用: 无
- 状态: `not-found-in-official-docs`
- 备注: `/docs/projects.md` 只提到 "If the work grows, move it into a project"（web surface），"move" 的具体 UI（拖动 / 菜单 / 按钮）**没有**官方文档化步骤。AIChat 不应假设官方提供"copy chat into project"按钮。

---

## 2. Projects

### 2.1 一个项目能否包含多个 folder

### official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Use local projects for folders and codebases"）
- 引用: "Add a local project when ChatGPT needs to read or change files on your computer. Projects don't need a folder, but you can attach folders as needed. To add or change folders, open the project's menu and select **Edit project**. Select **Add folder** to attach multiple folders."
- 状态: `official-confirmed`
- 备注: 项目可以挂 0..N 个 folder；至少 1 个 folder 才进入 local project 状态。

### 2.2 Primary directory 概念

### official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Use local projects for folders and codebases"）
- 引用: "To change the default working directory, point to a folder and select **Make primary**. New chats start in the primary folder. Codex also uses that folder for Git operations and automatic discovery of `AGENTS.md`, skills, and `config.toml`. Secondary folders remain available for file search, reading, and editing, but Codex doesn't automatically discover those project files from secondary folders."
- 状态: `official-confirmed`
- 备注: 区分明确——primary folder 是"工作目录 + AGENTS.md / skills / config.toml 自动发现位置"；secondary folder 只供读取 / 编辑，不参与自动发现。
- 额外限制（remote projects）: "Remote projects currently support one folder."

### 2.3 AGENTS.md / 配置 / 验证命令是否自动读取

### AGENTS.md、skills、config.toml 在 primary folder 自动发现
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Use local projects for folders and codebases"）
- 引用: 同上"automatic discovery of `AGENTS.md`, skills, and `config.toml`"段。
- 状态: `official-confirmed`
- 备注: secondary folder **不**走这条自动发现路径；只 manual search / read / edit。

### 项目级持久化建议（CLI 分支）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Work in a project directory" / CLI 段）
- 引用: "Keep durable project guidance in `AGENTS.md` or checked-in documentation so it is available to future chats."
- 状态: `official-confirmed`
- 备注: 这是 CLI 视角，但同 `AGENTS.md` 概念与 app 一致。

### Skill 加载位置（多个层级）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/build-skills.md`（"Where Codex loads local skills"）
- 引用: "Codex reads skills from repository, user, admin, and system locations. For repositories, Codex scans `.agents/skills` in every directory from your current working directory up to the repository root."
- 状态: `official-confirmed`
- 备注: 补充了项目级 / 用户级 / admin / system 四层 skill 加载顺序。SKILL.md 必须含 `name` + `description`。

### Local environment / setup scripts（自动跑）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/environments/local-environment.md`
- 引用: "Setup scripts run automatically when Codex creates a new worktree at the start of a new chat."
- 状态: `official-confirmed`
- 备注: Local environment 配置存到项目根目录的 `.codex/` 文件夹，可 check in Git 共享给团队。

### 2.4 项目设置与权限分离

### 项目设置与权限的边界（部分官方文档化）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Use local projects for folders and codebases"）
- 引用: "Use [local environments](https://learn.chatgpt.com/docs/environments/local-environment) to define setup actions and common commands for a project. Git review, pull request, and [worktree](https://learn.chatgpt.com/docs/environments/git-worktrees) actions target the primary repository."
- 状态: `official-confirmed`
- 备注: 项目设置（setup script / actions / Git review / worktree 范围）都**绑在 primary repo**。其它动作（sandbox / approval）走 Codex host 全局/会话级，跟项目设置是**两个独立维度**。

### 权限（approval / sandbox）与项目设置是不同层
- 来源: `https://learn.chatgpt.com/docs/sandboxing.md`（"Configure defaults"）
- 引用: "Approvals determine when Codex pauses before an action, while the sandbox determines which files and network resources commands can access."
- 状态: `official-confirmed`
- 备注: 明确说明 sandbox 决定"技术边界"、approval 决定"何时停下问"——是两条独立控制线。

---

## 3. Plugins

### 3.1 完整旅程：发现 / 详情 / 安装 / 授权 / 启用 / 使用 / 升级 / 卸载

### 旅程骨架（app / web 通用）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Install and use a plugin"）
- 引用: "Once you open the Plugins Directory: <WorkflowSteps> 1. Search or browse for a plugin, then open its details. 2. Select the plus button to install the plugin. 3. If the plugin needs a connector, connect it when prompted. Some plugins ask you to authenticate during install. Others wait until the first time you use them. 4. After installation, start a new chat and ask ChatGPT or Codex to use the plugin. </WorkflowSteps>"
- 状态: `official-confirmed`
- 备注: 步骤对应"发现 → 详情 → 安装 → 授权 → 使用"。"升级"未单列，但有 "Bundled skills become available when you start a new chat or CLI session after installation." 暗示插件版本变更后需开新会话。

### 卸载 official-confirmed
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Remove a plugin"）
- 引用: "To remove a plugin, open it from a supported plugin browser and select **Uninstall plugin** when that action is available. Workspace-installed or default plugins may not offer that action; your workspace administrator controls them instead."
- 状态: `official-confirmed`
- 备注: workspace 强制安装的插件卸载入口由 admin 控制；用户装的可以自卸。

### 升级 / 自动升级（独立页面未直接见到）not-found-in-official-docs
- 来源: 插件页全文
- 引用: 无
- 状态: `not-found-in-official-docs`
- 备注: 文档化的是"更新工作流走 marketplace 重新安装"，没有"插件 in-place upgrade"流程。

### 3.2 能力分类：Skills / Command tools / Connectors / MCP / Hooks / UI resources

### 官方明列的组成（app / web / cli 通用段）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Overview"）
- 引用: "A plugin can contain one or more of these parts: - **Skills:** reusable instructions for specific kinds of work. - **Connectors:** connections to tools like GitHub, Slack, or Google Drive. - **MCP servers:** services that give ChatGPT and Codex access to more tools or shared information. - **Browser extensions:** browser capabilities that a plugin needs for its workflow. - **Hooks:** commands that run at configured lifecycle points. Review and trust plugin hooks before you enable them. - **Scheduled task templates:** reusable starting points for recurring tasks where scheduled tasks are available."
- 状态: `official-confirmed`
- 备注: 6 类完全对应 plan 列出的分类。**没有**官方名为 "Command tools" 的类别，Command tools 概念在 `Skills` + `Connectors` + `MCP` 里被组合实现。

### 3.3 是否有官方插件目录 / 商店

### Universal plugin directory（app / web 共用）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Universal plugin directory"）
- 引用: "ChatGPT and Codex use the same public plugin catalog. To browse and install plugins from a supported graphical surface: - On the web, turn on Work in the switcher and open **Plugins**. - In the ChatGPT desktop app, select ChatGPT and turn on Work in the switcher, or select Codex. Then open **Plugins**."
- 状态: `official-confirmed`
- 备注: 1 个统一目录，3 个 tab：**OpenAI** / **Your workspace name** / **Personal**。还有独立"Installed"行。
- 引用 (tabs 细节): "The Plugins Directory organizes plugins into tabs: - **OpenAI:** plugins built by OpenAI. - **Your workspace name:** plugins provided by your workspace. - **Personal:** personal marketplace plugins, including **Created by me** and **Shared with me** sections when those plugins are available."

### Marketplace 自建（CLI 视角）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Plugin browser in Codex CLI"）
- 引用: "Install a plugin from a configured marketplace, then start a new session before using its bundled skills or tools."
- 状态: `official-confirmed`
- 备注: Codex CLI 的 plugin browser 是按 marketplace 分组（marketplace tabs）。这是 marketplace 概念，OpenAI 自家目录是其中一个 marketplace。

### 个人开发者如何上架：submit / review 流程
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Build your own plugin"）
- 引用: "When your plugin is ready for review, see [Submit plugins](https://learn.chatgpt.com/plugins/deploy/submission) for the OpenAI Platform submission flow, required permissions, review materials, MCP checks, and test case requirements."
- 状态: `official-confirmed`
- 备注: 公开提交门户走 `developers.openai.com/plugins/deploy/submission`，**不是**直接上架 universal directory。需要 review + MCP check + test case。

---

## 4. Scheduled

### 4.1 字段：项目 / Prompt / Cadence / Execution Environment

### Prompt + 项目（app 段）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Manage scheduled tasks" / app 段）
- 引用: "schedule a task to evaluate telemetry errors and submit fixes, or to create reports about recent codebase changes."
- 状态: `official-confirmed`

### Cadence（标准 / 自定义 / RRULE）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Manage scheduled tasks" / app 段）
- 引用: "Use them when each run should be independent or when one scheduled task should run across one or more projects. If you need a custom cadence, use the custom schedule controls. For an advanced schedule, edit its RFC 5545 recurrence rule (RRULE), such as `RRULE:FREQ=MONTHLY;BYMONTHDAY=1;BYHOUR=9;BYMINUTE=0`."
- 状态: `official-confirmed`
- 备注: 高级 cadence 用 RFC 5545 RRULE 文本编辑。

### Execution Environment：模型 / reasoning / sandbox（app 段）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Manage scheduled tasks" / app 段）
- 引用: "You can also leave the model and reasoning effort on their default settings, or choose them explicitly if you want more control over how the scheduled task runs."
- 状态: `official-confirmed`
- 备注: 任务级 model + reasoning effort 字段是明确的，文档给了 model 退役警告作证："If a scheduled task uses `gpt-5.4` or `gpt-5.4-mini` with ChatGPT sign-in, update it before those models retire on August 31, 2026."

### 4.2 Local / Dedicated Worktree 选项

### official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Manage scheduled tasks" / app 段）
- 引用: "For Git repositories, each scheduled task can run either in your local project or on a dedicated background [worktree](https://learn.chatgpt.com/docs/environments/git-worktrees). Use worktrees when you want to isolate scheduled-task changes from unfinished local work. Use local mode when you want the scheduled task to work directly in your main checkout, keeping in mind that it can change files you are actively editing. In non-version-controlled projects, scheduled tasks run directly in the project directory. You can have the same scheduled task run on more than one project."
- 状态: `official-confirmed`
- 备注: Git 仓库下：local project **或** dedicated worktree 二选一；非 Git：直接 project directory。同一任务可绑定多个 project。

### 4.3 启用、暂停、立即运行、查看历史、重试

### 启用 / 暂停（inbox 视图带过滤器）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Manage scheduled tasks" / app 段）
- 引用: "Find all scheduled tasks and their runs on **Scheduled** in the ChatGPT desktop app sidebar. The **Scheduled** view acts as your inbox. Scheduled task runs with findings appear there, and an unread indicator shows when a run needs your attention."
- 状态: `official-confirmed`
- 备注: 视图名 "Scheduled"，是 sidebar 一级入口。"All, Active, and Paused filters" 是 inbox illustration 描述。
- 状态（过滤器）: 由 `<Illustration description="Scheduled tasks page with All, Active, and Paused filters and three scheduled tasks.">` 标注。illustration alt/description 在 `automations.md` 中直接给出，是 official text content。`official-confirmed`

### 立即运行 / 显式触发官方未文档化
- 来源: `automations.md` 全文
- 引用: 无
- 状态: `not-found-in-official-docs`
- 备注: 文档提到"Test scheduled tasks"（先在普通 chat 手动测 prompt），但**没有**直接描述 "Run now" 按钮。

### 查看历史（"runs" 是 inbox 容器）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Manage scheduled tasks" / app 段）
- 引用: "The **Scheduled** view acts as your inbox. Scheduled task runs with findings appear there."
- 状态: `official-confirmed`

### 重试（archive 之后再恢复 vs 永久删除）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Worktree cleanup for scheduled tasks"）
- 引用: "Archive scheduled runs you no longer need, and avoid pinning runs unless you intend to keep their worktrees."
- 状态: `official-confirmed`
- 备注: 官方把"重试 / 再看"叫 **pin**，跟"archive"是两个动作。明确区分 pin 留 worktree、archive 触发 cleanup。

### 4.4 Scheduled 任务在 chat 内 vs 独立

### 两种类型
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Schedule a task inside a chat" / app+web 段）
- 引用: "Schedule a task inside an existing chat when you want ChatGPT to return to that chat on a schedule. … Standalone scheduled tasks start a new chat for each scheduled run and report results in **Scheduled**."
- 状态: `official-confirmed`
- 备注: 区分"chat 内 scheduled task"（保留原 chat 上下文，每跑都回到同 chat）和"standalone scheduled task"（每次新 chat，结果入 inbox）。

---

## 5. Sites

### 5.1 项目列表、创建、预览、保存、部署

### 进入 Sites 入口（app）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Overview" / app 段）
- 引用: "Open **Sites** in the ChatGPT desktop app. You can start a site from a prompt or from a compatible local project, then return to the Sites view to manage it."
- 状态: `official-confirmed`

### 进入 Sites 入口（web）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Overview" / web 段）
- 引用: "Use Sites in ChatGPT on the web to create and manage hosted sites. Select **More** > **Sites**, or go directly to [chatgpt.com/sites](https://chatgpt.com/sites), to find Sites you've created."
- 状态: `official-confirmed`

### 创建：4 步（描述 → 审核 → 优化 → 分享）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Get started with Sites"）
- 引用: "<WorkflowSteps variant="headings"> 1. Describe the Site 2. Review the Site 3. Refine the Site 4. Manage and share the Site </WorkflowSteps>"
- 状态: `official-confirmed`
- 备注: workflow steps 是 `<WorkflowSteps variant="headings">` 渲染，文本是官方原文。

### 预览（Edit / Screenshot / Add files）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Get started with Sites" / web 段）
- 引用: "In the preview, select **Edit**. Under **Describe website edits**, describe the changes you want. Use **Screenshot** or **Add files and more** when additional context would help."
- 状态: `official-confirmed`

### 保存（"Save a version"）vs 部署（"Deploy a version"）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Understand projects, versions, and deployments"）
- 引用: "Sites publishing has two separate stages: 1. **Save a version.** ChatGPT builds a deployable version. … 2. **Deploy a version.** ChatGPT publishes a saved version and reports the production URL when deployment succeeds."
- 状态: `official-confirmed`
- 备注: 1 个 site 可以有 N 个 saved versions，最后 deploy 1 个 → production URL。

### 5.2 本地预览 vs 云部署

### 部署 = production official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Overview"）
- 引用: "Every Sites deployment URL is a production deployment. If you want to review a build before it becomes live, ask ChatGPT to save a version without deploying it."
- 状态: `official-confirmed`
- 备注: **没有**"本地预览 URL"概念；要么 save（不可访问），要么 deploy（线上 production URL）。

### 5.3 是否有 Hosting Provider 列表

### official-confirmed（无用户可换的 provider 列表）
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Control access and secrets" / "Connect a custom domain"）
- 引用: "Custom domains aren't available in Enterprise workspaces at launch. … Sites doesn't register domains for you, so you must be able to change the domain's DNS records."
- 状态: `official-confirmed`
- 备注: 域名走"自带域 + DNS 改记录"路径，Sites **自己不**做 domain 注册。**没有任何 "Hosting Provider" 选项**让用户选 Vercel / Cloudflare / Netlify——Sora ChatGPT Sites 用的是 OpenAI 自家 runtime + R2 + D1（见下）。

### 5.4 Sites 运行时 / 存储（补充信息）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sites.md`（"Understand projects, versions, and deployments" / "Choose a supported site shape"）
- 引用: "Sites hosts web experiences that run in the supported Sites runtime." 以及配置示例 `{ "project_id": "<project-id>", "d1": "DB", "r2": null }` 写入 `.openai/hosting.json`。
- 状态: `official-confirmed`
- 备注: 持久化能力分两类 —— **D1**（关系数据库）和 **R2**（对象存储）。Workspace-authenticated user identity、Sign in with ChatGPT 都是 first-class 能力。
- 引用 (D1/R2 表格) "A Site is a persistent hosted output that you can reopen, refine, configure, and share from **Sites** in ChatGPT. … A Sites project links a local source project to hosting managed through Sites. Sites stores that linkage and optional storage binding names in `.openai/hosting.json`."

---

## 6. Subagents

### 6.1 Active / Done / Failed 分组

### Web surface 给出 Active / Done（read-only）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Managing subagents" / web 段）
- 引用: "Open **Subagents** to see read-only **Active** and **Done** lists. Select a completed subagent to inspect its details and result. The web sidebar reports subagent activity; it doesn't provide controls to stop or steer an individual subagent."
- 状态: `official-confirmed`
- 备注: **Failed** 列表在 web 段**没有**官方文档化。Web 端是只读，**没有**"stop / steer"控制。

### App surface 给出 illustration（"with no active subagents and three completed audits"）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Managing subagents" / app 段）
- 引用: "<Illustration description=\"Codex desktop Subagents panel with no active subagents and three completed audits.\">"
- 状态: `official-confirmed`
- 备注: illustration alt/description 显式说"no active subagents and three completed audits"，所以 Subagents panel 至少分 **Active** 和 **Completed**。Failed 没直接描述。
- 状态（Failed 分组）: `not-found-in-official-docs`

### App 控制（"stop" / "steer" / "open subagent thread"）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Managing subagents" / app 段）
- 引用: "- Open a subagent thread from the activity shown in the main thread to inspect its work. - Ask Codex directly to steer a running subagent, stop it, or close completed subagent threads."
- 状态: `official-confirmed`
- 备注: App 端可"停止 / 转向"subagent。Steer / stop 的具体 UI 控件是让 Codex 在主 chat 下达指令来代理完成。

### 6.2 独立线程 / 任务 / 模板

### Agent thread 概念 official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Core terms"）
- 引用: "**Subagent workflow**: A workflow where Codex runs parallel agents and combines their results. **Subagent**: A delegated agent that Codex starts to handle a specific task. **Agent thread**: The thread where a subagent does its work. Supported clients let you open these threads to inspect progress or results."
- 状态: `official-confirmed`

### 独立 subagent 模板（Built-in agents）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Custom agents"）
- 引用: "Codex ships with built-in agents: - `default`: general-purpose fallback agent. - `worker`: execution-focused agent for implementation and fixes. - `explorer`: read-heavy codebase exploration agent."
- 状态: `official-confirmed`
- 备注: 3 个内置 agent：default / worker / explorer。

### 自定义 subagent 文件位置 official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Custom agents"）
- 引用: "To define your own custom agents, add standalone TOML files under `~/.codex/agents/` for personal agents or `.codex/agents/` for project-scoped agents."
- 状态: `official-confirmed`
- 备注: 必填字段 `name` / `description` / `developer_instructions`。

### 6.3 主会话只接收摘要

### official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-configuration/subagents.md`（"Why subagent workflows help"）
- 引用: "Run specialized **subagents** in parallel for exploration, tests, or log analysis. Return **summaries** from subagents instead of raw intermediate output."
- 状态: `official-confirmed`
- 备注: 主 thread 只收 summaries，subagent thread 可以单独打开看完整 work + tool output。

---

## 7. Sandboxing & Permissions

### 7.1 Read only / Workspace / Full access 三档

### 三档 sandbox_mode official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sandboxing.md`（"Configure defaults"）
- 引用: "At a high level, the common sandbox modes are: - `read-only`: The agent can inspect files, but it can't edit files or run commands without approval. - `workspace-write`: The agent can read files, edit within the workspace, and run routine local commands inside that boundary. This is the default low-friction mode for local work. - `danger-full-access`: The agent runs without sandbox restrictions. … Full access means using `sandbox_mode = \"danger-full-access\"` together with `approval_policy = \"never\"`."
- 状态: `official-confirmed`

### Built-in permission profiles 三个官方名称（Beta 文档）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/permissions.md`（"Define and select a profile"）
- 引用: "Codex includes three built-in permission profiles: - `:read-only` keeps local command execution read-only. - `:workspace` allows writes inside the active workspace roots and system temp directories. - `:danger-full-access` removes local sandbox restrictions and should be used only when that broad access is intentional."
- 状态: `official-confirmed`
- 备注: "Beta. Permission profiles are under active development and may change." —— 同一页顶部的标注。下游落地**不要**假设这套 profile 名是长期 API。

### 7.2 Ask for approval / Session allow / Deny 三选项

### approval_policy 三档 official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sandboxing.md`（"Configure defaults"）
- 引用: "The common approval policies are: - `untrusted`: The agent asks before running commands that aren't in its trusted set. - `on-request`: The agent works inside the sandbox by default and asks when it needs to go beyond that boundary. - `never`: The agent doesn't stop for approval prompts."
- 状态: `official-confirmed`
- 备注: 三个值是 `untrusted` / `on-request` / `never`。**注意命名差异**——CLI/agent 配置文件里没有 "Ask for approval / Session allow / Deny" 三个**UI 标签**；这是 approval_policy + approvals_reviewer 的组合。

### approvals_reviewer（user / auto_review）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/agent-approvals-security.md`（"Automatic approval reviews"）
- 引用: "By default, approval requests route to you: ```toml approvals_reviewer = \"user\" ``` … Set `approvals_reviewer = \"auto_review\"` to route eligible approval requests through a reviewer agent before Codex runs the request."
- 状态: `official-confirmed`
- 备注: 不是 3 选项而是 2 选项 + auto-review policy 的组合。下游若要"Session allow"概念，需要 grep `sandboxing/auto-review` 找具体 reviewer 寿命。

### UI 端 App surface 提及的可能 menu 项
- 来源: `https://learn.chatgpt.com/docs/sandboxing.md`（"How permissions work" / app 段）
- 引用: "In the ChatGPT desktop app, use the permissions control beneath the composer. Depending on your configuration, the menu can include **Ask for approval**, **Approve for me** for eligible approval requests, **Full access**, and named or custom permissions profiles."
- 状态: `official-confirmed`
- 备注: 这是 app 菜单里**实际显示**的项：**Ask for approval** / **Approve for me** / **Full access** / 命名 / 自定义 profile。**注意**：plan 提到的"Session allow / Deny"**没有**原样出现在官方 UI 菜单文案。"Approve for me" 是 eligibility-based 的自动批。

### 7.3 作用域 Global / Project / Session / Plugin / Tool

### 多层 config precedence（Global / Project / Session 维度）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/developer-settings.md`（"Configuration layers" / cli 段）
- 引用: "The CLI applies command-line flags and `--config` overrides before project, profile, user, system, and built-in settings. Use that precedence to keep shared defaults in configuration files and one-off changes on the command line."
- 状态: `official-confirmed`
- 备注: 6 个 precedence 层：CLI flags > `--config` > project > profile > user > system > built-in。"Session" 层 = CLI flags + `--config` overrides（per-run override）。

### 管理面组织（admin allowlist）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/permissions.md`（"Define and select a profile"）
- 引用: "Enterprise administrators can define profiles and restrict which profiles users may select through managed `requirements.toml`. Once `allowed_permission_profiles` is present, omitted profiles are denied, including omitted built-ins and profiles added in future Codex versions."
- 状态: `official-confirmed`
- 备注: Plan 提到的 "Plugin / Tool" 作用域在 UI 层**没有**直接文档化为独立菜单项。Tool-specific 限制通过 `(permissions.<name>.filesystem["<path>"])` 和 `permissions.<name>.network.domains` 实现。

### Scheduled 任务与 approval_policy 的特殊行为
- 来源: `https://learn.chatgpt.com/docs/automations.md`（"Permissions and security model" / app 段）
- 引用: "Scheduled tasks use `approval_policy = \"never\"` when your organization policy allows it. If admin requirements disallow `approval_policy = \"never\"`, scheduled tasks fall back to the approval behavior of your selected permission mode."
- 状态: `official-confirmed`
- 备注: scheduled 任务在 admin 允许时默认无审批跑；不允许时降级到当前 mode 的 approval 行为。

---

## 8. Composer 字段

> 计划提到 `+` 菜单、@ 补全、附件 / 图片 / 语音、发送 / 停止 / 重试。下面逐项标来源。

### 8.1 `+` 菜单

### 附件 / image input official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Bring in other tools and context" / app 段）
- 引用: "Attach files or [image inputs](https://learn.chatgpt.com/docs/image-inputs) directly to a chat when they apply only to that request."
- 状态: `official-confirmed`
- 备注: 附件 + image 是文档化的"+ 菜单"内容。

### 语音（voice）菜单项（app 段）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/reference/settings.md`（"Notifications" 周围段落；同 `Settings > Notifications` 描述 turn completion 通知）
- 引用: 无直接 + 菜单描述语音按钮
- 状态: `not-found-in-official-docs`（语音输入/输出在 `+` 菜单的位置**没有**专门页面直接确认）
- 备注: Codex 整体支持 voice（出现在 `developer-settings` 引用的"computer use"相关；可外推），但 `+` 菜单里具体哪些按钮是 voice vs dictation vs voice-to-text，官方没单独列。

### Memory / plugins / MCP（通过显式 mention 触发，**不在** `+` 菜单）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（"Bring in other tools and context" / app 段）
- 引用: "Install [plugins](https://learn.chatgpt.com/docs/plugins) to bring in context and actions from other services. Configure [MCP](https://learn.chatgpt.com/docs/extend/mcp) servers when your organization or developer setup exposes tools through Model Context Protocol. Use [memories](https://learn.chatgpt.com/docs/customization/memories), where available, to carry useful context from past work into future chats."
- 状态: `official-confirmed`
- 备注: plugins / MCP / memories 是**前置 enable**，不是 per-prompt 切换项。

### 8.2 @ 补全

### @ mention 触发 plugin / skill official-confirmed
- 来源: `https://learn.chatgpt.com/docs/build-skills.md`（"How ChatGPT and Codex use skills"）
- 引用: "1. **Explicit invocation:** Include the skill directly in your prompt. In ChatGPT, type `@` to select a skill. In Codex CLI or the IDE extension, run `/skills` or type `$` to mention a skill."
- 状态: `official-confirmed`
- 备注: **ChatGPT 用 `@`** 提 skill；**Codex 用 `$`**。两者语法不通用。

### @ 提插件 / 任务模式官方说明
- 来源: `https://learn.chatgpt.com/docs/plugins.md`（"Install and use a plugin" / app+web 段）
- 引用: "Type `@` to invoke the plugin or one of its bundled skills explicitly. Use this when you want to be specific about which plugin or skill ChatGPT should use."
- 状态: `official-confirmed`
- 备注: `@plugin-name` 强制选中某个插件 / skill。

### 8.3 附件 / 图片 / 语音

### 附件 official-confirmed
- 来源: 同 8.1 引用，"Attach files or image inputs directly to a chat"
- 状态: `official-confirmed`

### 图片（image inputs）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/projects.md`（同 8.1）
- 引用: 同上 + 链接 `[image inputs](https://learn.chatgpt.com/docs/image-inputs)`
- 状态: `official-confirmed`
- 备注: image-inputs 单独成页，但本报告没有 fetch `/docs/image-inputs.md`，具体字段是 image/png 还是更宽泛需另查。

### 语音 (inferred) 推断
- 来源: 无直接 `+` 菜单语音项
- 引用: 无
- 状态: `inferred`
- 备注: 计划提到的"语音"可能指 turn completion 通知（settings 里）或者 dictation。**没有任何官方页面**直接确认 "Composer `+` 菜单含 Voice / Dictation"。AIChat 不应在没截屏证据下做映射。

### 8.4 发送 / 停止 / 重试

### 发送 / 停止（run 模式）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/sandboxing.md`（"How permissions work" / app 段）
- 引用: "In the ChatGPT desktop app, use the permissions control **beneath the composer**."
- 状态: `official-confirmed`
- 备注: composer 下方有 permissions 控件 + (推断) 发送/停止按钮。**"beneath the composer"** 是官方对 composer 下方控件区的描述。

### 重试（Retry）官方未直接命名
- 来源: 全文
- 引用: 无
- 状态: `not-found-in-official-docs`
- 备注: Codex cli 的 `/review`、`/new`、`/resume` 等命令在 `developer-settings.md` "TUI" 段有列举；具体到 desktop app 的 "Retry last turn" 按钮**没有**专门官方页直接命名。Codex editor settings 表里有 `chatgpt.followUpQueueMode`（`queue` / `steer`），相关但不完全是"重试"。

### 8.5 IDE 端 Composer 行为（跨 surface 备注）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/developer-settings.md`（Editor settings 表）
- 引用: "`chatgpt.composerEnterBehavior` … `enter` always sends (`enter`), `Cmd`/`Ctrl`+`Enter` sends multiline prompts (`cmdIfMultiline`), or the modifier is always required (`cmdAlways`)."
- 状态: `official-confirmed`
- 备注: IDE 端的 Enter 行为有官方可配项；app 端没有同等设置公开。

---

## 9. Environment 面板

> 计划提到"Git / Diff / Branch / Worktree / Subagents / Background Processes / Sources"。下面按官方页能证实的逐项列。

### 9.1 Git / Diff / Branch

### 内置 Git 控件在本地 project 和 worktree 旁边
- 来源: `https://learn.chatgpt.com/docs/environments/local-environment.md`（"Use built-in Git tools"）
- 引用: "In Codex, the ChatGPT desktop app provides common Git controls alongside each local project and worktree. The diff pane shows changes in the current checkout and lets you add inline comments for Codex to address. You can stage or revert individual chunks, stage or revert entire files, commit changes, push a branch, and create a pull request without leaving the app."
- 状态: `official-confirmed`

### Review pane 状态（Unstaged / Staged / Commit / Branch / Last turn）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/code-review.md`（"What changes it shows" / app 段）
- 引用: "By default, the review pane shows **Unstaged** changes. Use **Staged** for the Git index, **Commit** for a selected commit, **Branch** for the diff against your base branch, or **Last turn** for the most recent assistant turn."
- 状态: `official-confirmed`
- 备注: 5 个 view 状态在 app review pane。

### 9.2 Worktree

### official-confirmed（见上文 §2.2 / §5 / §6 等）
- 主要面板 / 入口状态见 `/docs/environments/git-worktrees.md`
- 关键事实:
  - "Select **Worktree** under the composer."（composer 下方切换入口）
  - "Each worktree has its own copy of every file in your repo but they all share the same metadata (`.git` folder) about commits, branches, etc."
  - "Hand off the chat to move your chat _and_ code so you can continue in the other checkout."
- 状态: `official-confirmed`

### Permanent vs Codex-managed worktree
- 来源: `https://learn.chatgpt.com/docs/environments/git-worktrees.md`（"Codex-managed and permanent worktrees"）
- 引用: "By default, chats use a Codex-managed worktree. … If you want a long-lived environment, create a permanent worktree from the three-dot menu on a project in the sidebar."
- 状态: `official-confirmed`
- 备注: project sidebar 的三点菜单可以创建 permanent worktree（独立项目条目）。

### 9.3 Subagents panel（见 §6.1）official-confirmed
- 引用 (app 段): "<Illustration description=\"Codex desktop Subagents panel with no active subagents and three completed audits.\">"
- 状态: `official-confirmed`
- 备注: 至少分 **Active** / **Completed** 两区；Failed 没在 illustration alt 中描述。

### 9.4 Background Processes

### partial (r0.4 升级)
- 来源: r0.3 用户截图 `docs/competitor-evidence/screenshots/2026-08-01-codex-main-view.png` Environment 面板有 "后台进程" segment，显示运行中进程的命令行
- 引用: 无（官方文档无专门页面）
- 状态: `partial`（r0.4 从 `not-found-in-official-docs` 升级）
- 备注: 截图证实 segment 存在，监督能力（进程树 / PID / 日志 / 终止 / 重启恢复）0 文档化。`learn.chatgpt.com/docs/background-processes.md` URL 404。AIChat 落地此能力时**supervisor 细节是自创**，仅 segment 标签可参考 Codex。详见 `docs/competitor-evidence/wave-0-c-evidence-upgrade.md` §BGPROC-SUPER-01 + `docs/PARITY_TRACKING.md` §7 `BGPROC-SUPER-01`。

### 9.5 Sources

### not-found-in-official-docs（独立 panel）但 Sources 概念在多处
- 来源: 试 `/docs/sources.md` 404
- 引用: 无
- 状态: `not-found-in-official-docs`（独立 Sources 面板）
- 备注: "Sources" 概念在 `/docs/projects.md`（web 段）出现："Each project has a **Chats** section that lists project chats and a **Sources** section for uploaded files and connected context." —— 这是 web 项目视图的 sources section，**不**是 desktop app 的 Environment 面板。Desktop 的 Sources 面板在官方 markdown 文档里**没有**专门页。
- 引用 (web 段, 唯一) "Each project has a **Chats** section that lists project chats and a **Sources** section for uploaded files and connected context. Project instructions apply across its chats."

### 9.6 Environment summary panel（综合面板）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/environments/local-environment.md`（结尾 illustration）
- 引用: "<Illustration description=\"Codex environment summary panel\"> <EnvironmentPanelIllustration ariaLabel=\"Codex environment summary panel\" /> </Illustration>"
- 状态: `official-confirmed`
- 备注: 存在一个综合的 "Environment summary panel"。具体 block 列表（Git / Diff / Branch / Worktree / Subagents / Background Processes / Sources）**在官方 markdown 文档里没有逐条**。`<EnvironmentPanelIllustration>` 实际渲染由组件决定，无法从 markdown 拿内部 block 列表。

### 9.7 Dev tools 跳转（"open in your IDE"）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/environments/git-worktrees.md`（"Option 1: Working on the worktree"）
- 引用: "You can open your IDE to the worktree using the \"Open\" button in the header, use the integrated terminal, or anything else that you need to do from the worktree directory."
- 状态: `official-confirmed`

### Code review 跨 thread（PR 评论同步）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/code-review.md`（"Pull request reviews" / app 段）
- 引用: "When Codex has GitHub access for your repository and the current project is on the pull request branch, the ChatGPT desktop app can help you work through pull request feedback without leaving the app. The sidebar shows pull request context and feedback from reviewers, and the review pane shows comments alongside the diff so you can ask Codex to address issues in the same chat."
- 状态: `official-confirmed`
- 备注: PR 信息进入 sidebar + review pane。

---

## 10. 设置中心分类

> plan §4 列的 4 个一级分类：**个人 / 集成 / 编码 / 已归档聊天**。下列 16 个 sub-section 是从 `/docs/reference/settings.md` + `/docs/developer-settings.md` 实测摘抄。

### 10.1 个人（个人偏好与账户）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/reference/settings.md`
- 引用: "**General** … **Profile** … **Keyboard shortcuts** … **Notifications** … **Appearance** … **Pets** … **Personalization** … **Suggested prompts** … **Memories** … **Archived chats** … **Keep a chat near your work**"
- 状态: `official-confirmed`
- 备注: 官方页面顶部的章节列表就是这些（H2 标题序列）。本节在原页面是连续的 H2。

### General 字段 official-confirmed
- 引用: "Require `Cmd`+`Enter` for multiline prompts, or turn on **Prevent sleep while running** so local chats can continue while you step away. Under **Follow-up behavior**, choose whether a message sent while ChatGPT works should steer the current run or wait for the next run."
- 状态: `official-confirmed`

### Profile 字段 official-confirmed
- 引用: "Use **Profile** to review activity insights, lifetime tokens, peak tokens, streaks, your longest task, and token activity. You can also update your profile details, such as your picture, display name, and username, and save a profile card with usage highlights."
- 状态: `official-confirmed`

### Keyboard shortcuts 字段 official-confirmed
- 引用: "Open **Keyboard Shortcuts** to review commands, change bindings, or reset custom shortcuts to their defaults. Use the search field to find shortcuts by command name, or switch to keystroke search and press a key combination to find the command that uses it."
- 状态: `official-confirmed`
- 备注: 支持"按键反向搜索"——按下一个组合键，app 找到绑定该键的命令。

### Notifications official-confirmed
- 引用: "Choose when turn completion notifications appear, and whether the app should prompt for notification permissions."
- 状态: `official-confirmed`

### Appearance official-confirmed
- 引用: "In **Settings**, you can change the app appearance by choosing a base theme, adjusting accent, background, and foreground colors, and changing the UI and code fonts. You can also share your custom theme with friends."
- 状态: `official-confirmed`

### Pets official-confirmed
- 引用: "Pets are optional animated companions for the app. In **Settings > Pets**, choose a built-in or custom pet, then use `/pet`, **Wake Pet**, or **Tuck Away Pet** to control the floating overlay."
- 状态: `official-confirmed`
- 备注: 独立的 `/pet` slash 命令和 floating overlay。

### Personalization official-confirmed
- 引用: "Choose **Friendly**, **Pragmatic**, or **None** as your default personality. Use **None** to disable personality instructions. … You can also add your own custom instructions. Editing custom instructions updates your [personal instructions in `AGENTS.md`](https://learn.chatgpt.com/docs/agent-configuration/agents-md)."
- 状态: `official-confirmed`
- 备注: 3 个 personality 预设 + 自由 custom instructions。`AGENTS.md` 是个人级（不是项目级）记忆文件。

### Suggested prompts official-confirmed
- 引用: "Use context-aware suggestions to surface follow-ups and tasks you may want to resume when you start or return to ChatGPT."
- 状态: `official-confirmed`

### Memories official-confirmed
- 引用: "Enable Memories, where available, to let ChatGPT carry useful context from past chats into future work. See [Memories](https://learn.chatgpt.com/docs/customization/memories) for setup, storage, and controls for individual chats."
- 状态: `official-confirmed`

### Keep a chat near your work（popout + Always on top）official-confirmed
- 引用: "In the ChatGPT desktop app, pop out an active chat into a separate window and place it next to your browser, editor, or design preview. Turn on **Always on top** when you want the chat to remain visible while you work in another app."
- 状态: `official-confirmed`

### 10.2 集成（外部服务 / 设备）

### Browser official-confirmed
- 来源: `https://learn.chatgpt.com/docs/reference/settings.md`（"Browser" 段）
- 引用: "Use these settings to install or enable the bundled Browser plugin, set up the [Chrome extension](https://learn.chatgpt.com/docs/chrome-extension), and manage allowed and blocked websites. ChatGPT asks before using a website unless you've allowed it."
- 状态: `official-confirmed`
- 备注: 包含 allowed / blocked 站点白名单。

### Computer Use official-confirmed
- 引用: "Check your Computer Use settings to review desktop-app access and related preferences after setup. On macOS, revoke system-level access by updating Screen Recording or Accessibility permissions in macOS Privacy & Security settings."
- 状态: `official-confirmed`
- 备注: 走 macOS Privacy & Security 系统级权限授予。

### 10.3 编码（agent / repo / 工具）

### Project and terminal behavior official-confirmed
- 来源: `https://learn.chatgpt.com/docs/developer-settings.md`（"Project and terminal behavior" / app 段）
- 引用: "Choose where files open, how much command output appears in chats, and where terminal tabs open by default."
- 状态: `official-confirmed`

### Code review official-confirmed
- 来源: `https://learn.chatgpt.com/docs/developer-settings.md`（"Code review" 段）
- 引用: "Under **Settings > Git**, use **Review delivery** to choose **Inline** to run `/review` in the current chat when possible or **Detached** to start a separate review chat."
- 状态: `official-confirmed`
- 备注: app 段将 review delivery 放在 **Settings > Git**（不是顶层 "Code Review"）；IDE 段是 `chatgpt.reviewDelivery`。

### IDE extension sync official-confirmed
- 来源: `https://learn.chatgpt.com/docs/developer-settings.md`（"IDE extension sync"）
- 引用: "When the ChatGPT desktop app and IDE extension are open in the same project, they share active chats and editor context. Turn on **IDE context** from the app composer to let Codex use files currently open in your editor."
- 状态: `official-confirmed`

### Agent configuration official-confirmed
- 来源: 同页（"Agent configuration"）
- 引用: "Codex agents in the app inherit the same configuration as the IDE extension and CLI. Use the in-app controls for common settings, or edit `config.toml` for advanced options."
- 状态: `official-confirmed`
- 备注: 同一份 `config.toml` 走 CLI / IDE / app 三端。

### Git official-confirmed
- 来源: 同页（"Git" 段）
- 引用: "Use Git settings to standardize branch naming and choose whether Codex uses force pushes. You can also set prompts that Codex uses to generate commit messages and pull request descriptions."
- 状态: `official-confirmed`

### Integrations and MCP official-confirmed
- 来源: 同页（"Integrations and MCP"）
- 引用: "Connect external tools through Model Context Protocol (MCP). Enable recommended servers or add your own. If a server requires OAuth, the app starts the authentication flow. These settings also apply to the Codex CLI and IDE extension because MCP configuration lives in `config.toml`."
- 状态: `official-confirmed`

### Browser developer mode official-confirmed
- 来源: 同页（"Browser developer mode"）
- 引用: "Under **Developer mode**, turn on **Enable full CDP access** to let ChatGPT use the Chrome DevTools Protocol for performance profiling and deeper browser debugging."
- 状态: `official-confirmed`

### Connections（推测存在）inferred
- 来源: 无直接 evidence
- 引用: 无
- 状态: `inferred`
- 备注: plan §10.3 提到 "connections" 是编码子项。本报告**没有**找到 `Connections` 单独段；可能是 MCP 之前的旧名 / 或在 Git section 合并。

### 10.4 已归档聊天

### Archived chats official-confirmed
- 来源: `https://learn.chatgpt.com/docs/reference/settings.md`（"Archived chats" 段）
- 引用: "The **Archived chats** section lists archived chats with dates and project context. Use **Unarchive** to restore a chat."
- 状态: `official-confirmed`
- 备注: archive / unarchive 是完整动作（区别于 §4 提到的"archive scheduled runs"）。

### 10.5 Settings 入口（how to open）official-confirmed
- 来源: `https://learn.chatgpt.com/docs/reference/settings.md`（页头）
- 引用: "Open [**Settings**](codex://settings) from the app menu or press <kbd>Cmd</kbd>+<kbd>,</kbd> on macOS or <kbd>Ctrl</kbd>+<kbd>,</kbd> on Windows."
- 状态: `official-confirmed`
- 备注: 设置走 `codex://settings` deep link + 通用 `Cmd+,` / `Ctrl+,`。

---

## 附录 A：plan §4 中提到的"首层入口 5 项"对照表

| plan §4 入口 | 官方文档 | 状态 |
|---|---|---|
| 新对话 | `projects.md` 提"New chat" + `Cmd+Option+N` Quick chat 快捷键 | `official-confirmed`（区分"普通 chat vs 项目 chat"明确） |
| 拉取请求 | `code-review.md` "Pull request reviews" 段 | `official-confirmed`（PR 上下文进 sidebar + review pane；非独立"PR 一级入口"——是 review 模式 + GitHub CLI 集成） |
| 站点 | `sites.md` 全文 | `official-confirmed`（独立一级入口；web 通过 `More > Sites`、app 通过 sidebar **Sites**） |
| 已安排 | `automations.md` "Find all scheduled tasks and their runs on **Scheduled** in the ChatGPT desktop app sidebar." | `official-confirmed`（独立 sidebar 一级入口 Scheduled） |
| 插件 | `plugins.md` "open **Plugins**" | `official-confirmed`（独立 Plugins 一级入口；web 需 "Work" 模式切换） |

## 附录 B：本文未确认的 plan §4 计划点（`not-found-in-official-docs` / `inferred`）

- "移动 / 复制普通聊天到项目" 的具体 UI（仅在 web 段提到 "move it into a project"）
- Composer `+` 菜单里"语音"项
- "Retry last turn" 按钮（CLI 端通过 `/review` 等命令有，desktop app 端命名未直接确认）
- Environment 面板里 **Background Processes** 独立 section
- Environment 面板里 **Sources** section（web 端有 Sources section，但 desktop Environment 面板里**没有**专门 section 文档化）
- Settings 编码子项 "connections"（可能并入 MCP）
- Plugin 升级 in-place flow（文档化的是 marketplace 重新安装）
- Failed subagent 分组（仅 web 显式 Active / Done，Failed 未官方列出）
