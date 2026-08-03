# AIChat 重构计划（daily driver 化）

> **Status: 历史计划 / 已废止。** 本文档保留为历史参考，但**不再作为产品方向**。
>
> - 产品目标已被 **[`docs/CODEX_DESKTOP_PARITY_PLAN.md`](CODEX_DESKTOP_PARITY_PLAN.md)** 取代：Codex Desktop 操作对等（12 Wave）取代了"对标 ClaudeCode 完整度 + Linear/Notion 气质"。
> - §0 "现状速写"（文件体量 / 死代码清单）作为清理证据仍有参考价值，可在 Wave 1 schema 迁移前的工程债盘点中复用。
> - §1–§4 的阶段计划（止血 / 拆 harness / 死代码清理 / 气质收敛）已下线，部分工作已由 `git log` 中 `front-end MVP pass`（commits `c0d0bf8..323d281`）和"真的很劣质"清理 wave（`320cedc` / `e356ee5` / `8d98050`）零散完成。
> - 主开发线下一步以 [parity 追踪表](PARITY_TRACKING.md) 的 Wave 0 退出条件为准。
>
> **不要**再按本计划的阶段顺序推进新工作；只允许作为"已完成清理 vs 未完成清理"的对照表使用。

> 目标对标 ClaudeCode 完整度 + Linear/Notion 私人工具感。
> 阶段顺序：先收口可见债 → 再清死代码 → 再做气质。每一阶段都自带
> build + test 验收门槛，不留"半重构"半成品。

## 0. 现状速写

| 区块 | 体量 | 状态 |
|---|---|---|
| `AIChat.App.Avalonia/ViewModels` | 19 个文件 / 4797 行 | 顶层 3 个 VM 占 2018 行（`MainWindow`/`AgentHost`/`AgentRunner`），god class 边缘 |
| `MainWindow.axaml` + `.axaml.cs` | 803 + 629 | XAML 与 code-behind 双向耦合：快捷键 / 滚动 / 焦点管理全在 ctor |
| `AgentHarness.cs` | **1973** | 单方法 `RunAsync` 把规划 / 子 agent / 主循环 / 落盘 / 质量评估 / 恢复包全包了 |
| `AgentRunner.cs` | 363 | 重试 + 工具执行 + 错误处理三层嵌套，事件流硬塞 return / yield return |
| `ViewModels/SlashCommandHandler.cs` | 210 | 直接 `new MainWindowViewModel` 类型的入参，反向依赖宿主 |
| `Agents/Benchmark/*` | 7 文件 / ~30 类 | **零调用**（仅 `AgentRun.QualityScore` 等被 PerformanceSummary/HistoryInsight 读，UI 从不消费） |
| `Application/Audit/*` | 3 文件 | **零调用**（grep `AgentRunAudit` / `AuditEventRecorder` 在 src 全部为零） |
| `Application/Verification/VerificationResultParser.cs` | 1 文件 | 唯一引用方是 `AgentHarness.RecordVerification`，本体可内联 |
| `Application/Workspace/WorkspaceRestoreBatchRunner.cs` + `WorkspaceCommitBatchRunner.cs` | 2 文件 | 与 `WorkspaceChangeService.RestoreFileAsync` / `CommitAsync` **重复** |
| `Application/Plugins/` (MCP / PluginToolProvider) | 18 文件 | 完全没 UI surface（`ProviderConfigViewModel` / `SettingsViewModel` 都不读），用户开箱用不到 |
| `Application/InputArtifacts/*` | 5 文件 / 419 + 171 行 | `InputArtifactService` 一肩挑 kind 推断 + 文本提取 + 摘要 + OCR + 重删，419 行单文件 |
| `Agents/SubAgents/SubAgentScheduler.cs` | 200 | 只跑 explorer 模板，DAG 调度是空跑道（注释自己都承认） |
| `Application/Configuration/AdvancedSettingsService.cs` | ~150 | 与 `ProviderSettingsService` 字段重复，normalize 各做一次 |
| `Abstractions/Configuration/AppSettings.cs` | 65 | 30+ 字段，`ApiKey` / `ProtectedApiKey` / `ApiKeyProtection` 三件套实际只用一个 |
| 测试 | 106 文件 / 15899 行 | 覆盖 Services/Application 不错；`Avalonia/` 子目录只有 4 个 VM-level test（`SlashCommandHandler` / `GitStatus` / `ConversationList` / `RunSummaryBuilder`） |

## 1. 重构目标

把这套东西变成一个**日常用的工具**，而不是一个有真功能的 demo：

1. **响应硬性 ≤ 100ms**：键入 prompt、滚活动 feed、切项目、开关只读模式 — UI 不卡顿
2. **可读代码 ≤ 1 屏**：每个文件 ≤ 400 行；每个方法 ≤ 80 行
3. **删除死代码**：所有"pluggable but unwired"的子系统和重复路径清出去
4. **保证可观测**：每个用户操作都有可追踪的反馈（toast / status / system bubble）
5. **气质收敛**：UI 动效、字距、间距、配色都从 `Tokens.axaml` 出，XAML 零 inline 硬编码
6. **测试反射形**：每个核心路径都有 DI-only test，避免 UI-headless 才有 coverage

---

## 阶段 1：止血（先别崩）

> 改动小、风险低、收口日常使用中真会踩的坑。1 周内可推完。

### 1.1 修 `ViewLocator` 的反射

- 文件：`src/AIChat.App.Avalonia/ViewLocator.cs`
- 问题：`Activator.CreateInstance` + `[RequiresUnreferencedCode]` + 命名约定"VM 改 View"是 string-replace，模板生成期一炸就 NRE
- 改成：手写 `Dictionary<Type, Func<Control>>` 显式注册；或改用 Avalonia 11+ 的 `IDataTemplate` 强类型（保留 reflection 但走 `Type.GetType` 而不是 `FullName.Replace`，并缓存到 `ConcurrentDictionary`）
- 验证：AppHostTests + 启动时 `GetRequiredService<MainWindow>()` 不抛

### 1.2 修 `SlashCommandHandler` 的反向依赖

- 文件：`src/AIChat.App.Avalonia/ViewModels/SlashCommandHandler.cs`
- 问题：210 行 + 静态方法 + 拿整 `MainWindowViewModel`（`BuildStatus` 读 8 个宿主字段）
- 改成：拆 `ISlashCommandHost` 接口（`ActivityFeed` / `Sidebar.CurrentProject` / `ConversationList.Conversations` / `ActiveProvider` / `ActiveModel` / `NoWriteMode` / `StatusMessage` / `AgentHost.ContextBudgetPercent` / `AgentHost.LastAssistantStatus` / `HasClipboardService` / `CopyToClipboardAsync` / `GetGitStatusSummaryAsync`）；`MainWindowViewModel` 实现该接口
- 验证：`tests/Avalonia/SlashCommandHandlerTests.cs` 不变就过（它已经用 `MainWindowViewModel` 实际实例化）

### 1.3 修快捷键 → 命令的 contract 测试

- 文件：`src/AIChat.App.Avalonia/Views/MainWindow.axaml.cs:49-227`
- 问题：15 个 `KeyBindings.Add(new KeyBinding { ... RelayCommand(async () => ...) ... })`，每个 lambda 都重新跑 `SlashCommandHandler.TryExecuteAsync` + 写 ActivityFeed + 改 StatusMessage
- 改成：抽 `KeyCommandBridge` 静态 helper：单一入口 `await host.RunSlashCommandAsync("/copy")`，XAML/快捷键都调它
- 验证：每个 `/copy` / `/git` / `/help` / `/clear` 路径都走同一份代码

### 1.4 修 `_ = RecomputeContextInputTokensAsync` fire-and-forget 链

- 文件：`src/AIChat.App.Avalonia/ViewModels/AgentHostViewModel.cs:647-676` + `MainWindowViewModel.cs` 多处
- 问题：每次 prompt 键入、project 切换、no-write 切换都 `_ = RecomputeContextInputTokensAsync(...)`；body 里 `Task.Run` 抛了会 crash（AGENTS.md 已经提过）
- 改成：
  1. 内部 `try/catch` 已存在，再加一个外层 `try/finally` 保证 CTS 不漏
  2. 用 `SemaphoreSlim` 限制 200ms 去抖（多次键入只算最后一次）
  3. 失败时除 StatusMessage 外加 toast（跟 SendTask 失败对齐）
- 验证：`tests/Avalonia/AgentHostViewModelTests.cs`（新建）覆盖 happy + 失败路径

### 1.5 修 `OnNoWriteModeChanged` / `OnProviderTestStarted` 改 host-语义字段

- 文件：`src/AIChat.App.Avalonia/ViewModels/MainWindowViewModel.cs:425-427` 附近
- 问题：之前 PR 修过"测试时 IsRunning 被 clobber"但还有边界：approval 弹窗期间 `IsRunning` flip / `LastAssistantStatus` 多次改写
- 改成：把"测试中"和"agent 运行中"用两个独立 bool；`OnProviderTestStarted` 改 `IsProviderTesting`，UI 用它去 disable send 按钮；agent 内部 status flip 唯一发生地在 `AgentRunnerViewModel` 那个 finally
- 验证：单元 + 手动跑 ⌘T 期间按 send 不应启动新 run

### 1.6 测试基线锚定

- 新建 `tests/Composition/AppHostTests.cs` 已有，新增对所有顶层 service 的 `GetService<T>()` 断言（`IApprovalService` / `IThemeService` / `IToastService` / `IWorkspaceChangeService` / `IChatCompletionService` / `IAppRepository` / `AgentToolRegistry` / `RoutedChatCompletionService` / `ProviderConnectionTester` / `MainWindowViewModel` / `AgentHostViewModel` / `AgentRunnerViewModel` / `ConversationListViewModel` / `SettingsViewModel` / `ToolApprovalViewModel` / `MemoryEditorViewModel` / `GitStatusViewModel` / `ProjectSidebarViewModel` / `ProviderConfigViewModel`）
- 锁住"DI 漏注册"再次发生

---

## 阶段 2：拆 AgentHarness god class

> 这一步最重，但要动 1973 行单文件。拆完主流程可读 3-5 倍。

### 2.1 拆 `AgentHarness.RunAsync` 为编排者

- 文件：`src/AIChat.Application/Agents/AgentHarness.cs`
- 改成：保留 `RunAsync` 作为编排（**单方法 ≤ 100 行**），按阶段抽：
  - `PlanPhase`（`+planner`）→ 处理 `AgentHarnessEventType.StepAdded (Planner)`
  - `ContextPhase` → `CreateContextStepOutput` / `ApplyExecutionPolicy` 内联
  - `SubAgentPhase`（`+_subAgentScheduler`）→ `ComputeSubAgentExecutionLayers` / `BuildSubAgentTask` / `RecordSubAgentArtifact` / `FormatSubAgentResult` / `AppendSubAgentResultMessage`
  - `ToolLoopPhase` → 当前 `await foreach` 主循环
  - `WrapUpPhase` → `CompleteFinalValidation` / `CompleteQualityAssessment` / `CompleteTelemetry` / `CompleteRecoverySuggestion` / `BuildFinalContent` / `CreateFinalStatusReason` / `CreateIncompleteRunUserMessage`
- 每个 phase 接收 `AgentHarnessContext`（run + policy + settings + 工具），返回 `IAsyncEnumerable<AgentHarnessEvent>`，编排者 `await foreach` 透传

### 2.2 内联 11 个 `*Builder.cs` 到 2-3 个语义类

- 文件：
  - `AgentRunCheckpointSummaryBuilder.cs` (223)
  - `AgentRunDiagnosticSummaryBuilder.cs` (130+)
  - `AgentRunHistoryInsightBuilder.cs` (148)
  - `AgentRunPerformanceSummaryBuilder.cs` (116)
  - `AgentRunTelemetryBuilder.cs` (149)
  - `AgentExecutionPolicySummaryBuilder.cs` (45)
  - `AgentSmokeTestChecklistBuilder.cs` (170)
- 改成：合并到 `AgentRunSummary` 静态类，按"用途"分方法（`ForTelemetry` / `ForCheckpoint` / `ForPolicySummary` / `ForRunHistory`），每个方法 ≤ 40 行
- **同时删掉** `AgentRunDiagnosticSummaryBuilder` / `AgentRunHistoryInsightBuilder` / `AgentRunPerformanceSummaryBuilder` / `AgentSmokeTestChecklistBuilder` — 4 个**零调用方**的 builder（grep 全文 `using` 引用皆为 0）

### 2.3 删 `Agents/Benchmark/*` 全员 + `AgentRun` 13 个死字段

- 文件：
  - `Application/Agents/Benchmark/*.cs` 7 个
  - `Domain/Chat/AgentRun.cs`：`QualityScore` / `QualitySummary` / `StrategySuggestion` / `AcceptanceNote` / `Telemetry` / `OutcomeKind` / `CheckpointSummary` / `CheckpointArtifactRefs` / `VerificationRecoveryPacket` / `RecoverySuggestion` / `FinalValidationSummary` / `CompletionEvidenceStatus` / `CompletionEvidenceSummary` / `CanClaimModified` / `CanClaimVerified` / `HistoryInsightSummary` / `PerformanceSummary` / `DiagnosticSummary` / `SmokeTestChecklist`
  - `Application/Agents/AgentRunQualityEvaluator.cs` (120+)
  - `Application/Agents/AgentStrategyAdvisor.cs` (130+)
  - `Application/Agents/AgentCompletionEvidenceChecker.cs` (130+)
  - `Application/Agents/AgentSmokeTestItem.cs`（如果只有这个 class）
  - 相关 `Builder.cs`（步骤 2.2 删的 4 个）
- **同时删** `Application/Agents/Coordinator/AgentCoordinator.cs` (318) 中 `StartPhase` 之外的部分（DAG / 阶段机），保留 `PhaseTransition` 最小版给 status bar 用
- 验证：grep `已删的子系统名` 在所有源文件都 0 hits
- 风险：UI 显示"上次运行"依赖 `LastAssistantStatus`（保留），不依赖这些字段
- 验证：现有 15899 行测试不能挂；删 1 个测试文件可能挂（`AgentBenchmarkEvaluatorTests` 如果存在）→ 跟删同步

### 2.4 拆 `AgentRunner` 的三段嵌套

- 文件：`src/AIChat.Application/Agents/AgentRunner.cs:37-323`
- 改成：抽 3 个 helper：
  - `TrySendChatRoundAsync(...)`（处理重试 + transient + 终端错误）→ 返回 `ChatRoundResult { RawEvents, Content, Reasoning, ToolCalls, TerminalEvent, Succeeded }`
  - `ExecuteToolRoundAsync(...)`（处理 budget + execution service + step recording）→ 返回 `ToolRoundResult`
  - 主 `RunAsync` 就是 `while (true) { var chat = await TrySendChatRoundAsync(...); if (chat.TerminalEvent != null) yield return; break; var tools = await ExecuteToolRoundAsync(...); if (tools.Stop) break; }`

### 2.5 拆 `MainWindowViewModel` 的"host 杂货" → 多 ViewModel

- 文件：`src/AIChat.App.Avalonia/ViewModels/MainWindowViewModel.cs:831` 仍残留 `HasUnseenMessages` / `UnseenMessageLabel` / `Greeting` / `SubGreeting` / `IsReady` / `NeedsConfiguration` / `Readiness` / `ActiveProvider` / `ActiveModel` 这些纯展示状态
- 改成：建 `AppStatusViewModel`（`IsReady` / `Readiness` / `ActiveProvider` / `ActiveModel` / `IsProviderTesting` / `HasUnseenMessages` / `UnseenMessageLabel` / `Greeting` / `SubGreeting`），订阅 `Sidebar.SelectedProjectName` / `Settings.AutoVerify` / `Provider.*` / `AgentHost.LastAssistantStatus`
- 这样 `MainWindowViewModel` 只剩"modals open flags" + `RegisterCommandPaletteCommands` + `Refresh` 三个真职责

---

## 阶段 3：清理死代码 + 子系统瘦身

> AGENTS.md 标记"已删的子系统"清单全部移除；修一些 naming 跟语义对齐。

### 3.1 删 `Application/Audit/*` 全员

- 文件：`src/AIChat.Application/Audit/AgentRunAuditService.cs` / `AgentRunAuditLoader.cs` / `AuditEventRecorder.cs`
- 验证：grep `AuditLog` / `AgentRunAudit` 在 src 全部为 0
- 保留：仅 `Abstractions/Persistence/IAuditLogRepository.cs` + `Storage.Json/AuditLogRepository.cs`（已经 wiring），但 `AppSettings.AuditLogMaxFileSizeBytes` / `AuditLogRetentionDays` 字段也删（无消费者）

### 3.2 删 `WorkspaceRestoreBatchRunner` / `WorkspaceCommitBatchRunner`

- 文件：
  - `Application/Workspace/WorkspaceRestoreBatchRunner.cs`
  - `Application/Workspace/WorkspaceCommitBatchRunner.cs`
- 验证：grep `WorkspaceRestoreBatchRunner` / `WorkspaceCommitBatchRunner` 在 src 为 0
- 实际调用走 `IWorkspaceChangeService.RestoreFileAsync` / `CommitAsync` 已有的实现

### 3.3 删 `SubAgents/DAG` 部分

- 文件：`Application/Agents/SubAgents/SubAgentScheduler.cs` + `Application/Agents/Coordinator/AgentCoordinator.cs` + `AgentHarness.ComputeSubAgentExecutionLayers`
- 改成：保留 `SubAgentScheduler.RunAsync` 单 template 单 run 形式；DAG 调度、DependsOn、并行执行层 — 全删（注释自己说"今天 explorer 不带依赖，DAG 永远单层"）
- 风险：低；UI 看不到 DAG

### 3.4 删 `Application/Plugins/*` 中**没接的**部分

- 保留：
  - `Plugins/PluginToolProvider.cs`（基础设施）
  - `Plugins/PluginManifest.cs` + `Loader.cs` + `Validator.cs`（已通过 `AgentToolRegistry.CreateDefaultWithPluginsAsync` 接入）
  - `Plugins/PluginCommandTool.cs`（命令型插件）
- 删：
  - `Plugins/Mcp/McpStdioClient.cs` + `McpStdioServerConfig.cs` + `McpToolCallResult.cs` + `McpToolDescriptor.cs` + `PluginMcpTool.cs` — MCP **零调用**（grep `McpStdioClient` 在 src 全为 0）
  - `Plugins/PluginSkill.cs` + `PluginSkillLoader.cs` + `PluginSkillManifest.cs` — skill 体系零调用
- 风险：低；用户根本不知道有这些
- 后续：MCP 在阶段 4 重新接（接在 settings → "外部工具"）

### 3.5 合并 `AdvancedSettingsService` ↔ `ProviderSettingsService`

- 文件：
  - `Application/Configuration/AdvancedSettingsService.cs`
  - `Application/Llm/Routing/ProviderSettingsService.cs`
- 现状：两边都做 normalize；`AdvancedSettingsService` 调 `ProviderSettingsService.Normalize` 后再调一次
- 改成：单 `ProviderSettingsService.Normalize(settings)`，删 `AdvancedSettingsService`
- 验证：`tests/Configuration/AdvancedSettingsServiceTests.cs` 跟着删

### 3.6 收 `AppSettings` 30+ 字段

- 文件：`src/AIChat.Abstractions/Configuration/AppSettings.cs`
- 删：
  - `ApiKey`（保留 `ProtectedApiKey`）— 两个并存但 `ApiKeyProtection` 实际只是空字符串
  - `ApiKeyProtection` — 设为常量保护机制，已 dead
  - `AuditLogMaxFileSizeBytes` / `AuditLogRetentionDays` — 阶段 3.1 删 audit
  - `AgentAdaptiveStrategiesEnabled` / `AgentAdaptiveBudgetAndExplorerEnabled` / `AgentAdaptiveRecoveryEnabled` / `AgentAdaptiveAutoVerifyEnabled` — 阶段 2.3 删 coordinator
- 跟着：所有 `OnXxxChanged` partial 同步收口

### 3.7 收 `InputArtifactService`

- 文件：`Application/InputArtifacts/InputArtifactService.cs` (419)
- 改成：拆 3 个：
  - `InputArtifactClassifier`（kind + 文本提取）— 90 行
  - `InputArtifactSummarizer`（build summary）— 70 行
  - `InputArtifactService` 主体只保留 CRUD + 引用方法 — 100 行
- 同步：`InputArtifactFileStore` / `InputArtifactVisionPolicy` 单文件 ≤ 80 行（当前已较瘦）

### 3.8 合并 `ToolExecutionService` ↔ `AgentToolRegistry`

- 文件：
  - `Application/Tools/AgentToolRegistry.cs` (203)
  - `Application/Tools/ToolExecutionService.cs` (269)
  - `Application/Tools/AgentToolCatalog.cs`（如已存在）
- 改成：`AgentToolRegistry` 一肩挑 registry + execution orchestration；`ToolExecutionService` 内联为 `AgentToolRegistry.ExecuteAsync`
- 风险：低；UI 看不到

---

## 阶段 4：气质收敛（XAML / 设计系统 / 活人感）

> 不再删代码，把视觉 / 动效 / 反馈曲线拉到 Linear / Notion 私人工具的密度。

### 4.1 XAML 零 inline 硬编码

- 文件：所有 `*.axaml` / `*.axaml.cs`
- 验证：`grep -nE '#[0-9A-Fa-f]{3,8}\b' src/AIChat.App.Avalonia/Views/` 应只有 `Tokens.axaml` / `Tokens.Dark.axaml`
- 改：把所有 `Fill="#xxxxxx"` / `Stroke="#xxxxxx"` / `Background="#xxxxxx"` 替换为 `Brush="{StaticResource ...}"`

### 4.2 收 19 个 ViewModel 的事件链

- 文件：`ViewModels/*` 一堆 `+= OnXxx` / `-= OnXxx`
- 现状：`MainWindowViewModel` 订阅 8+ 个其他 VM 的事件；事件多了之后难追
- 改成：改成 `MainWindowViewModel` 显式 `WireSubscriptions()` / `UnwireSubscriptions()` 集中管理；IDisposable

### 4.3 重建空白态 + empty state

- 文件：`Views/MainWindow.axaml:243-380` 的 empty state 区
- 现状：4 个 quick action card + 2 个 first-run CTA + hero + sub-greeting 已经 130+ 行
- 改成：抽 `Views/Controls/EmptyStateView.axaml` user control，主窗引用 — 留 1 行 `<views:EmptyStateView />`

### 4.4 重建 /help 文档

- 文件：`ViewModels/SlashCommandHandler.cs:21-29` 的 `HelpBody` 常量
- 改成：把 help 文本移到 `Resources/HelpText.md` 或 .axaml 的 string resource — 改文档不用重编

### 4.5 静默 bug 收敛

- 删 `MainWindow.axaml.cs` 中"墓碑注释"段（AGENTS.md 提到过）
- 删 `OnApprovalPresented` / `OnApprovalResolved` 中无操作分支
- `MainWindowViewModel.GetGitStatusSummaryAsync` 改走新 `IWorkspaceChangeService` 不读 `project.Path` 字符串（更稳）

### 4.6 性能 / UI 反馈曲线

- 任何 `await Task.Run(...)` 路径加 elapsed 反馈（隐藏慢查询）
- `ContextRouter` / `ProjectFileIndexBuilder` 缓存命中显示在 status bar
- Activity feed 长会话（> 1000 行）加虚拟化（`ItemsControl` 改 `VirtualizingStackPanel`）

### 4.7 接回 MCP / Plugins（可选，看产品决定）

- 在 `ProviderConfigViewModel` 加"MCP Servers"区
- `PluginToolProvider` 的 LoadFromDirectoryAsync 已经能跑
- `AppHost` 加 `AppData/plugins/` 目录监听

---

## 5. 不要做的事

1. **不要**做"功能完整度对标 ClaudeCode"前先上 UI 重写
2. **不要**加新功能前没真需求 — 任何"UI 没功能就是噪音"的新加都删
3. **不要**做 A2A / 远程 / 多端 — `docs/A2A_ADAPTER_DESIGN.md` 标记过未到 daily driver 阶段
4. **不要**全模型评测 / 对比 — `Benchmark/*` 删了就是删了
5. **不要**动 `Application/Projects/ProjectInitializer.cs` 之外的项目加载 — 这块逻辑 OK
6. **不要**重写主键绑定 — 阶段 1.3 只把 lambda 抽出来，XAML 维持

## 6. 顺序 & 验收

每阶段完工门槛：

- **阶段 1**：build + 所有 test pass + 手动 ⌘O/⌘T/⌘⇧M/⌘⇧G/⌘⇧C/⌘⇧V/⌘R/⌘L/⌘. 全通
- **阶段 2**：build + test pass + `AgentHarness.cs` ≤ 600 行 + 主 `RunAsync` ≤ 100 行
- **阶段 3**：build + test pass + grep `已删的子系统名` 0 hits + 启动时间 < 1.2s
- **阶段 4**：build + test pass + dark mode 手动验过 + 启动 < 1s

预计节奏：

| 阶段 | 估计 commits | 大致窗口 |
|---|---|---|
| 1 止血 | 6-8 | 1 周 |
| 2 拆 harness | 4-6（每次 1 个 phase） | 2 周 |
| 3 清死代码 | 8-10 | 1.5 周 |
| 4 气质 | 6-8 | 1 周 |
| **合计** | ~30 commits | 5-6 周 |

每 commit 独立、buildable、可回滚；CI 全程绿。
