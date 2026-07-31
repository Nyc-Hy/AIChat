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
| ⌘⇧V | 切换自动验证 |
| ⌘⇧C | `/copy` — 复制最后一条 AI 回复 |
| ⌘⇧M | 打开 memory editor modal |
| ⌘O | 添加项目（打开 folder picker） |
| ⌘T | 测试当前模型（连接性测试） |
| ⌘G | `/git` — 当前分支 + 变更列表（bubble） |
| ⌘⇧G | 打开 git status / diff viewer modal |
| ⌘/ | 显示 /help |
| F5 | 刷新状态（重读本地项目 + 对话） |
| ⌘V | 粘贴图片 → pending attachment（⌘↵ 一起送） |
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

## 代码 pitfall 类（2026-07-31 清理 wave 总结）

本轮（commits `798c03d`..`65cc00b` + 第二轮 `c46272e`..`3dee14a`）系统性扫出来的 bug 模式，下次写新 VM / 新 XAML 之前先看一遍。

### 1. PropertyChanged propagation gap

Avalonia binding 只在源属性 `PropertyChanged` 时重求值。**派生属性需要显式 re-raise**，否则 UI 卡在初始值。

- `[ObservableProperty]` 源 → 用 `[NotifyPropertyChangedFor(nameof(Derived))]`，例：
  - `InputTokens` → `ContextBudgetPercent` + `ContextBudgetWidthInMini`（状态栏进度条）
  - `LastAssistantStatus` + `IsRunning` → `CanRetry`（retry 按钮 IsVisible）
  - `HasPendingApproval` → `ApproveCommand` + `RejectCommand` + **`ApproveForSessionCommand`**（漏过最后这个就 session-allow 按钮永远灰着）
  - `UnseenMessageCount` → `HasUnseenMessages` + **`UnseenMessageLabel`**（pill 文本不显示数字）
- `ObservableCollection` 源（Clear/Add/Remove）→ mutation 后**手动 `OnPropertyChanged(nameof(Derived))`**，例：
  - `PlanItems.Clear/Add` 后 re-raise `HasPlan` + `PlanCompletedCount` + `PlanProgressText`（plan 面板不显示）
  - `SubAgentRuns.Clear` 后 re-raise `HasSubAgentRuns`（sub-agent 区域不消失）
  - `OnPropertyChanged` callback 里订阅外部事件（`sidebar.PropertyChanged`）→ re-raise `IsAvailable` / `ProjectName` / `EmptyStateMessage`
- 集合方法（`Remove` / `Clear` / 重新 `Add`）→ 都得记得 re-raise
- **不会出 PropertyChanged 的 collection-only 变化要手动 fire** —— 之前 `SeedEmptyState()` 和 `RefreshAsync()` 并发 fire `_ = RecomputeContextInputTokensAsync(...)`，两个 fire-and-forget 互相覆盖，删掉冗余的那个就消除了竞态

### 2. Schema "set but never bound"

`AppSettings` 字段加了、normalize 了、persistence 保留了、但**构造点忘了读**——schema 撒谎。

本轮找到并修：
- `UseTokenizerEstimation`（`AgentRunnerViewModel.RunAsync` 之前直接 `new TokenizerContextEstimator()`）
- `LastActiveConversationId`（`MainWindowViewModel.RefreshAsync` 没传给 `_conversationList.Refresh` 的 preferredConversationId；`OnConversationSelected` 也要回写）
- `RetryMaxAttempts`（`new AgentRunner(...)` 之前没传 `retryPolicy`）
- `MaxOutputTokens` for OpenAI 路径（`payload` 构造时没塞 max_tokens；Anthropic 路径 OK）

下次新加 schema 字段，写完三件事后第四件必须做：
1. 在 `AppSettings.cs` 加属性
2. 在 `ProtectedSettingsSerializer.Clone` 加 clone 一行
3. 在 `AdvancedSettingsService` / `ProviderSettingsService` 加 normalize 一行
4. **找到构造点把字段读进去**——grep `new <Constructor>` 的调用点，看哪些 hardcode 了默认值要换成读字段

### 3. DI 漏注册

`IWorkspaceChangeService` 自 `dad7384` / `8712d63` 引入但没人注册到 DI，`MainWindowViewModel` / `GitStatusViewModel` 都在 ctor 注入，app 启动 `GetRequiredService<MainWindow>()` 直接抛 `InvalidOperationException`——**app 打不开**。`SlashCommandHandlerTests` 走完整 `AppHost.Build()` DI 图能锁住这类 bug，下次新加 service 时顺手在 `AppHostTests` 加一行 `GetService<T>()` 断言。

### 4. 状态混淆（state confusion）

事件 handler 不该改不属于自己语义的状态。`OnProviderTestStarted` / `OnProviderTestCompleted` 改 `IsRunning`（agent-run 状态）→ 测试时 send/stop 按钮乱跳 + 用户跑 agent 时点测试会导致 IsRunning 被 clobber → 第二个 run 能并发开。**写新事件 handler 之前先问：我要改的字段名跟我这个事件语义匹配吗？**

### 5. UI 承诺 → 实际 binding

`ToolTip.Tip="..."` / `Shortcut="..."` / palette 的 `Shortcut` 列 / page header 的 pill → 都得对得上一个**实际**触发代码（`KeyBindings.Add` / `Command` / partial void）。本轮 sweep 找到 6 个 mismatch（⌘O / ⌘T / ⌘⇧M 重复 / ⌘⇧G 重复 / ⌘⇧C / ⌘⇧V），全部接上了。下次新加快捷键 / 按钮承诺，**两件事要一起做**：XAML tooltip + `KeyBindings.Add` 或 `Command=` binding，写完 grep 一遍确认两边都有。

### 6. 墓碑注释（tombstone comments）

描述"已删的代码"为什么删 / 它做了什么。git log 已经有完整 history，源码里再写一次就是噪音。`SessionInsightsViewModel` 删的时候留下的 3 段 `(no-op metrics update — see comment on the event-handler version above...)` 全部清除。下次大块 delete 时，**注释也一起删**，不要留指向幽灵代码的指针。

### 7. 跨线程 UI 突变（thread-safety）

`async IAsyncEnumerable`（`AgentHarness.RunAsync`）的 `await foreach` 消费者可能跑在非 UI 线程。`[ObservableProperty]` 的 setter、绑定的 `ObservableCollection` 的 `Add/Remove/Clear`、`OnPropertyChanged(nameof(...))` 都要在 UI 线程上执行。**Avalonia 抛 "Collection was modified during enumeration" 或默默丢掉 PropertyChanged 都属于这一类**。

- `AgentRunnerViewModel.ApplyAgentEventAsync` 的三个 `_updatePlan` 调用（StepAdded / SubAgentCompleted / ToolResult）以前直接 fire 在 harness 的 worker 线程上 → 状态栏的计划面板偶尔显示陈旧。修法：抽 `UpdatePlanOnUiThreadAsync(plan)` helper 把 `Dispatcher.UIThread.InvokeAsync` 收成一处
- `RecomputeContextInputTokensAsync` 是 fire-and-forget（`OnDraftPromptChanged` / `OnNoWriteModeChanged` / `OnSidebarProjectSelected` / `OnSidebarProjectAdded` / `SendTaskAsync` / `RefreshAsync` 都 `_ =` 它），body 里有两个 `Task.Run`（file index + context router），任何一个抛（perms / 卸载的盘）就会让 app 直接 crash。**async void / fire-and-forget 路径的每个 body 都得 try/catch 一次**，跟 847a598 的 XAML handler 同级

## 测试基线

`621 → 693`，覆盖：
- 4 个 `SlashCommandHandlerTests`（走完整 DI 图，覆盖 `MainWindowViewModel` 的 4 个 slash 命令），还锁住了 DI 漏注册 bug
- 1 个 `MemoryEditorViewModelTests` 锁住 `[NotifyCanExecuteChangedFor(AddCommand)]` on `ErrorMessage`（add 失败后 Add 按钮立刻灰着，再敲字就重新亮）
- 1 个 `GitStatusViewModelTests` 锁住 `LastUpdatedDisplay` + `HasLastUpdated` 的 OnLastUpdatedChanged re-raise

下次给新加的 service 写测试时，跟着 `AppHost.Build()` + `GetRequiredService<T>()` 模式走，能间接把整个 ctor 链跑一遍。

## 已知遗留（不动）

- **Planned-but-unwired 子系统**：`Application/Audit/*`、`Application/Diagnostics/*`、`Application/Agents/Benchmark/*`、`WorkspaceChangeService.RestoreFileAsync` / `CommitAsync`、`AgentRun.QualityScore` / `StrategySuggestion` / `AcceptanceNote` 等十几个字段——按"风险高于收益"判断，没动。**任何删除 / 重构之前先 grep `已删的子系统名` 确认没人读**。
- **Dark mode 视觉验证**：需要 GUI 跑起来看，agent 没访问。
- **TextBox 高度 cosmetic**：低优先级。

