# AIChat

本文件由 AIChat 自动生成，用于帮助 AI Agent 理解本项目。

## 技术栈

- C# / .NET

## 目录结构

```text
.claude/
artifacts/
docs/
src/
tests/
```

## 构建

```bash
dotnet build AIChat.sln --no-restore -m:1 -v:minimal
```

## 测试

```bash
dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal
```

## Git

本项目使用 GitHub Issues、Pull Requests 和 CI 管理开发流程。
请使用聚焦的分支，并保持 Pull Request 足够小，便于评审。

## 约定

- 遵循项目现有代码风格和模式。
- 提交前运行构建和测试。
- 使用能清楚描述变更的提交信息。
- 不要提交本地密钥、日志、安装包、`.tools/`、`.vs/`、`bin/` 或 `obj/`。
- GitHub 工作流详见 `CONTRIBUTING.md` 和 `docs/GITHUB_WORKFLOW.md`。

## Daily driver 当前能力（codex/desktop-rebuild 分支）

### 键盘快捷键

| 快捷键 | 功能 |
|---|---|
| ⌘K | 命令面板 |
| ⌘, | 设置 |
| ⌘N | 新建对话 |
| ⌘⇧T | 切换主题 |
| ⌘↵ | 发送任务 |
| ⌘. | 停止当前任务 |
| ⌘R | 重试上一次任务 |
| ⌘L | 聚焦 prompt（SelectAll） |
| ⌘⇧K | 清空对话（运行中禁用） |
| ⌘⇧R | 切换只读 / no-write 模式 |
| ⌘/ | 显示 /help |
| ⌘V | 粘贴图片 → pending attachment（⌘↵ 一起送） |
| ⌘G | `/git` — 当前分支 + 变更列表（bubble） |
| ⌘⇧G | 打开 git status / diff viewer modal |
| ⌘⇧M | 打开 memory editor modal |
| Esc | 关闭命令面板 / 设置 / memory / git modal（按优先级） |

### Slash 命令

- `/clear` `/new` — 清空 activity feed
- `/help` — 命令清单
- `/status` — 项目 / 模型 / Context / 上次运行
- `/memory` — 当前项目 memory 列表
- `/git` `/git-status` — 当前 git 状态
- `/copy` — 复制最后一条 AI 回复到剪贴板

### Modals

- **命令面板 (⌘K)** — 模糊搜索，跳到对应面板或动作
- **设置 (⌘,)** — provider / model / API key / no-write / auto-verify
- **Memory editor (⌘⇧M)** — 当前项目 memory 增删，按 category 分组
- **Git status / diff (⌘⇧G)** — 左文件列表 / 右 diff viewer，可复制
- **Tool approval** — 写入工具被 agent 触发时弹窗，三选一：拒绝 / 允许一次 / 本会话内允许

### Sub-agent

- DAG 分层调度（`AgentHarness.ComputeSubAgentExecutionLayers`）→ 独立 sub-agent 并行
- Plan panel 里 sub-agent 段显示 template + task + 时长 + 状态（颜色：running=accent / completed=绿 / failed=红 / budget=黄 / cancelled/skipped=灰）

### 附件

- `⌘V` 粘贴图片到 prompt，缩略图显示在 composer 上方
- 发送时升级为 `InputArtifact`（image/png），通过 `AgentRequestFactory` 注入 vision-capable 模型的 user message
- `@file 路径` 引用内联文件内容（已存在）
- pending-attachments 启动时清空 stale 文件

### 运行反馈

- 跑完 activity feed 里追加 `本次运行` system bubble："改 N 个文件 · 用 N 次工具 · 派 N 个子 Agent · 12s"
- 工具错误 → 工具问题 bubble
- 失败 → toast + assistant bubble 状态=失败；可 ⌘R 重试
- 已停止 → toast + assistant bubble 状态=已停止；可 ⌘R 重试

### 重要修复

- `d3a0600` — tool approval modal 缺位，写入工具一上来就 hang
- `847a598` — async void event handler 没 try/catch，任意 throw 整个窗口崩

## 产品定位（2026-07-30 用户原话）

**AIChat 是 daily driver,要完全替代 ClaudeCode。** 这不是 demo,不是实验场,不是玩票。

含义:
- "AI 味太重 / 活人感" 这类反馈是**核心产品定位**,不是个人偏好。任何 UI 改动都得问:这让一个每天开 8 小时的人用着更舒服,还是更花哨?
- **功能完整度对标 ClaudeCode**:agent loop、工具执行、代码编辑、流式响应、上下文管理、tool approval,这些不是 nice-to-have,是产品本身
- **美学对标 Linear / Notion**:私人工具感、企业级克制,不要 SaaS / AI-startup 调性
- 任何"加新东西"的决定都要先回答:背后有真功能吗?没有就删掉,UI 没功能就是噪音
