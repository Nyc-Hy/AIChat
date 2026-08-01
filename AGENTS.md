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
| ⌘? | 打开键盘快捷键 cheat sheet（也=titlebar ? 按钮） |
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
- **Keyboard shortcuts (⌘?)** — 18+ 个快捷键 cheat sheet，按类别分组（任务 / 项目 / 命令 / 模式 / 工具审批 / Slash）
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
- 失败 → AI bubble 底边变红 + soft red wash + status chip 变红（`bubble-ai.failed` class）；可 ⌘R 重试
- 已停止 → AI bubble 底边变琥珀 + soft amber wash + status chip 变琥珀（`bubble-ai.stopped` class）；可 ⌘R 重试
- **Conversation list rename** — 右键对话 → 重命名 → TextBox 内联编辑（Enter / focus-lost 提交，Esc 取消）。Rename 通过 `Func<id, newTitle, Task>` 回调到 `ConversationListViewModel` 持久化

### Empty states

- Sidebar: "(还没有项目,按 ⌘O 添加)" / "(还没有对话)"
- CommandPalette: "(无匹配命令)"
- 主面板：EmptyStateView — 首屏 hero + 2 个 first-run CTA（添加项目 / 配置模型），加载项目后切到 4 个 quick-action card

### Loading / inline feedback

- GitStatusView 刷新按钮：进行中切 0.8s 旋转 spinner（Grid swap Path 子元素）
- SettingsView provider 测试：3 行互斥反馈（in-flight muted / SuccessBg / ErrorBg）+ 上次结果记忆
- 失败/已停止 AI bubble：见上"运行反馈"

### 重要修复

- `d3a0600` — tool approval modal 缺位，写入工具一上来就 hang
- `847a598` — async void event handler 没 try/catch，任意 throw 整个窗口崩

## Front-end MVP pass（codex/desktop-rebuild, 2026-08）

`c0d0bf8` 起 9 个 commit，目标是消除 daily driver 残留的"卡 / 迷"瞬间。详细分类：

### 1. Empty / 零状态文案

让用户**没项目**时知道下一步：sidebar `(还没有项目,按 ⌘O 添加)` / `(还没有对话)`、CommandPalette `(无匹配命令)`。技术上用 `ObjectConverters.Equal=0` 模式（不需要新 IsEmpty 属性）。

### 2. Loading / inline feedback

用户按下"测试"按钮要立刻看到 spinner，否则会反复点：GitStatusView 刷新按钮切 0.8s 旋转 Path、SettingsView provider 测试 3 行互斥反馈（in-flight muted / SuccessBg / ErrorBg）。`ProviderConfigViewModel` 加 `IsTestInFlight` / `LastTestMessage` / `LastTestIsSuccess` / `LastTestHasResult` 4 个字段，try/finally 保证 IsTestInFlight 一定复位。

### 3. Tool approval Esc/Enter

之前点完 prompt 后 agent 弹 tool approval，用户**必须**鼠标点 3 个按钮之一。`ToolApprovalView.axaml` 加 `IsVisible="{Binding HasPendingApproval}"` + `KeyDown` handler + Tooltip 提示，Esc=拒绝、Enter=允许一次。"本会话内允许"故意没绑快捷键（会和"按 S 发送"肌肉记忆撞）。

### 4. Settings tool permission presets

15 个 tool 一个一个点 dropdown 是体力活。3 个 preset 按钮："只读自动" / "全部确认" / "恢复默认"，SettingsViewModel 加 3 个 `[RelayCommand]` 复用 `AgentToolRegistry.AllWithMetadata` 批量改 mode。

### 5. Help button + KeyboardShortcutsView modal

titlebar `?` 按钮 + ⌘? 全局快捷键，调出 18+ 快捷键 cheat sheet（任务 / 项目·导航 / 命令·信息 / 模式·设置 / 工具审批 / Slash 命令 6 个 section）。XAML hard-code（不是 VM-bound，因为是 documentation 不是 state），`Esc` 或点击 scrim 关闭。`App.axaml` 加 `TextBlock.kbd-display` 样式（FontMono + TextBrush），和 `kbd-pill` 视觉一致但走 TextBlock 路径。新加 `xmlns:behaviors` 别名（`AIChat.App.Avalonia.Behaviors`）。

### 6. AI bubble 错误/停止视觉

之前 failed AI bubble 跟 in-flight time-stamp 视觉一样（都 `muted` 灰字），扫 feed 容易漏。`ActivityItemViewModel` 加 `IsFailed` / `IsStopped`（`Status == "失败" / "已停止"` 派生），`OnStatusChanged` 同步 re-raise。`App.axaml` 加 `Border.bubble-ai.failed` / `.stopped` 样式（ErrorBorderBrush/Background 改色）+ `TextBlock.muted.failed/stopped` 状态 chip 改色。XAML 用 `Classes.failed="{Binding IsFailed}"` 切换。

### 7. Conversation list inline rename

右键只有"删除"——攒 3 个对话就分不清。`ConversationCardViewModel` Title 从 get-only 改 `[ObservableProperty]`，加 `IsRenaming` / `EditingTitle` + `StartRename/CancelRename/CommitRenameAsync` 4 个命令。Callback 模式（`Func<string, string, Task>?`）让 card 不直接知道 repository。新加 `Behaviors/FocusOnLoadBehavior.cs`（AttachedProperty `IsEnabled`），双 trigger：AttachedToVisualTree（首次 attach）+ IsVisible changed（重命名 → 提交 → 再重命名 时 TextBox 已在 visual tree 里，只 IsVisible flip）。`ConversationListViewModel.RenameConversationAsync` 走 `_repository.SaveProjectsAsync` 持久化，含"new 占位卡 / 空字符串 / 未变化"3 个 no-op 短路。

### 8. Avalonia 12 命名空间坑

`Behaviors/FocusOnLoadBehavior.cs` 因为文件在 `AIChat.App.Avalonia.Behaviors` 命名空间下，`Avalonia.VisualTreeAttachmentEventArgs` 的 `Avalonia.` 前缀会被编译器解析为同 namespace 子命名空间——加 `using Avalonia;` + bare name 解决。

### 9. /help body 外部化

`/help` 走的是 `Resources/HelpText.md`（`EmbeddedResource`），不是 AvaloniaResource——`AssetLoader` 在 headless test host 不 init。

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

## "真的很劣质" cleanup wave（commits `320cedc` / `e356ee5` / `8d98050`，2026-08-01）

用户看了 file tree + preview 的 5 commit 之后反馈"真的很劣质"——截图显示 3 个明显 bug 叠在一起。根因都是"binding chain 静默失败"，但失败点各不相同。下次新加 UserControl / ViewModel 之前先看一遍。

### A. UserControl 必须显式设 `DataContext`，否则 binding 跑回主 VM

`<vcontrols:FilePreviewView/>` 和 `<vcontrols:FileTreeView/>` 在 MainWindow.axaml 里没设 `DataContext`，继承 MainWindowViewModel。XAML 内的 `{Binding HasFile}` / `{Binding Root}` / `{Binding IsBuilding}` 在 MainWindowVM 上找不到对应属性——binding silently 失败，IsVisible fallback 到 `true`（default），于是：

- FilePreviewView 永远 visible 显示 "正在读取文件..." 幽灵（即使没选文件）
- FileTreeView 同时显示 "(选择一个项目查看文件)" + "正在建立文件索引..." 两个相互矛盾的提示

**规则**：所有放在 MainWindow 里的 `vcontrols:XxxView` 必须在 use-site 显式 `DataContext="{Binding XxxViewModelProp}"`——不能依赖"DataContext 自然继承"。

### B. UserControl 内 `DataContext = this` 是反模式

`EmptyStateView` 的 ctor 写了 `DataContext = this`，意图是让内部 XAML `{Binding Greeting}` 解析到自己的 styled property。**但这样 host（MainWindow）的 `Greeting="{Binding AppStatus.Greeting}"` 也在 EmptyStateView 自己的 DataContext 上求值**——EmptyStateView 没有 `AppStatus` 属性，binding 再次 silently 失败，**Greeting setter 从来没被调用过**，hero TextBlock 永远空着。

**修法**（最干净）：

1. 删 `DataContext = this`
2. root UserControl 加 `x:Name="Self"`
3. 内部 XAML 用 `{Binding #Self.Greeting}` / `{Binding #Self.HasProject}` / `{Binding #Self.OpenSettingsCommand}` 引用 styled property
4. host 那边的 `Greeting="{Binding AppStatus.Greeting}"` 走继承的 MainWindowViewModel，正常 work

`x:CompileBindings="False"` 不能绕过这个问题（试过没用）——binding 链的 source 解析跟 compile / reflection 模式无关，是 DataContext 决定了 source。

### C. Collection 改动要 fire PropertyChanged，不是 CollectionChanged

`Sidebar.Projects` 是 `ObservableCollection<ProjectCardViewModel>`。`AppStatusViewModel` 之前只订阅了 `PropertyChanged`（只盯 `SelectedProjectName` 变化）。`ObservableCollection.Clear()` / `Add()` 触发 `CollectionChanged`，**不会** 触发 `PropertyChanged("Projects")`。后果：

- 启动时 sidebar 加载项目 → `Projects.Add` → 集合有 1 项 → `AppStatus.HasProject` 仍然是 `false`（初始值，没人 re-raise）→ 4 quick-action cards 不显示 / hero 文案错

**修法**：AppStatusViewModel 还要 `_sidebar.Projects.CollectionChanged += OnSidebarProjectsChanged;` 主动 fire `OnPropertyChanged(nameof(HasProject))` 等。

### D. Event 不在所有 transition 都 fire = 启动时功能残废

`ProjectSidebarViewModel.ProjectSelected` 事件**只**在 `SelectProjectAsync`（user click handler）里 fire，`ApplyProject` 私有方法不 fire。结果：

- 启动 `Refresh(projects) → ApplyProject(target)` 静默更新 `CurrentProject` / `SelectedProjectName` → FileTreeViewModel 收不到通知 → 不 rebuild → 文件树空着显示 "(选择一个项目查看文件)"（即使项目已选）
- 用户必须**手动再点一次项目卡**才看到文件树

**规则**：状态转换的 event firing 必须在**所有**改状态的入口里覆盖（启动恢复、user click、user add、user remove、null transition），不能只在 user-driven 的那个里 fire。`ReferenceEquals(previous, current)` guard 避免重复 fire。

### E. HasProject 单一字符串判定 vs 真实状态

`HasProject` 之前只检查 `Sidebar.SelectedProjectName` 不是空、不是 "未配置路径"。但 sidebar 的 `SelectedProjectName` 默认值就是 `"未选择项目"`（display hint）——这个字符串既非空也不是"未配置路径"，**所以默认 HasProject = true**。

结果：fresh install 没项目时，empty state 直接显示 "今天要完成什么？" + 4 quick-action cards（HasProject=true 分支），CTAs（添加项目 / 配置模型）反而被隐藏。

**修法**：HasProject 加 `Projects.Count > 0` gate + 排除 display-hint 字符串：

```csharp
public bool HasProject => _sidebar.Projects.Count > 0
                          && !string.IsNullOrWhiteSpace(_sidebar.SelectedProjectName)
                          && _sidebar.SelectedProjectName != "未配置路径"
                          && _sidebar.SelectedProjectName != "未选择项目";
```

**教训**：display-only 字符串 ("未选择项目" / "无" / "—") 跟 semantic state 不要共用同一个字段。要么拆 `DisplayName` + `IsEmpty`，要么让 IsEmpty 走独立判据。

### F. macOS .NET 10 `SpecialFolder.ApplicationData` = `~/Library/Application Support`

**不是** Linux 习惯的 `~/.config`。调试时在这个 path 写测试数据没用，必须写到 macOS 那个。

下次开发测试需要写 settings / projects.json，先 `Environment.SpecialFolder.ApplicationData` 确认路径，不要凭 Linux 习惯直接 `~/.config`。

### G. Avalonia 12 styled property 不会被父 binding 透过 DataContext 抓到

`AvaloniaProperty.Register<TControl, TValue>(...)` 注册的 styled property 在 XAML 里写 `Greeting="{Binding AppStatus.Greeting}"` 是 **单向的 source → target** 流程（source = host's AppStatus.Greeting, target = control's Greeting styled property）。Avalonia 不为 parent DataContext 跟 child DataContext 不一致的情况做特殊透传——

`#Self.Greeting` 模式（root UserControl `x:Name="Self"` + `{Binding #Self.Greeting}`）是 Avalonia 12 官方推荐的自引用方式，比 `RelativeSource AncestorType=UserControl` 干净。



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

