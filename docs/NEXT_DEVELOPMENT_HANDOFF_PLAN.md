# AIChat 下一阶段开发交接计划

本文档面向能力较低或上下文较短的模型。目标是让它们可以按小任务稳定推进 AIChat，而不是一次性做大重构。

当前策略：先优化，再新增。优先降低维护成本、补测试、整理文档和提升可观测性；新增功能必须建立在现有 Harness、权限、审计、验证体系上。

## 当前项目状态

AIChat 是一个 .NET 8 WPF 桌面应用，用于项目级 LLM 聊天和本地代码 Agent 工作流。

主要能力已经完成：

- 项目级会话和设置。
- OpenAI-compatible 与 Anthropic Provider。
- Agent Harness、模型工具循环、工具审批、工具预算。
- 文件读写、搜索、patch、git、build/test、shell 等内置工具。
- 项目路径保护、shell 风险拦截、工具权限模式。
- Agent 运行历史、计划、文件变更、验证结果、恢复建议、复盘包。
- JSON 持久化与 JSONL 审计日志。
- 项目文件索引、上下文包、pinned context。
- 发布和架构文档基础。

最新检查结果：

```powershell
dotnet test AIChat.sln --no-restore
```

应通过 268 个测试。

## 开发原则

1. 保持分层：UI 只放在 `AIChat.App`，Agent 编排和工具逻辑放在 `AIChat.Application`，领域模型放在 `AIChat.Domain`，协议适配放在 `AIChat.Providers.*`，持久化放在 `AIChat.Storage.Json`。
2. 不要继续扩大 `MainViewModel.cs`。新增核心逻辑先放 Application 或独立服务，UI 只绑定状态和命令。
3. 每次只做一个小目标。不要同时改 UI、Provider、Harness、Storage。
4. 工具和 Agent 相关改动必须考虑权限、审计、恢复、测试。
5. 写文件、shell、git 修改类功能必须保持保守，不能绕过 `ProjectPathGuard` 和审批机制。
6. 不要删除或回滚用户未提交改动。开始前先看 `git status --short`。
7. 文档改动也要跑 `git diff --check`。

## 推荐执行顺序

建议按下面顺序推进。每个任务都可以单独提交。

1. 整理文档和计划，降低交接成本。
2. 拆分 `MainViewModel` 中的纯逻辑，降低 UI 文件复杂度。
3. 补齐审计日志查看能力，提升 Agent 可观测性。
4. 补 Provider 兼容测试，降低模型协议差异风险。
5. 优化上下文索引和上下文包表现，提升 Agent 任务命中率。
6. 发布准备和版本体验打磨。

## 任务 1：整理旧开发计划文档（✅ 已完成）

优先级：高。

目标：让后续模型不要被旧阶段计划误导。

背景：`docs/REMAINING_DEVELOPMENT_PLAN.md` 现在包含大量已完成阶段，同时中间仍残留旧的阶段 7-16 详细计划，阅读成本高。

建议修改文件：

- `docs/REMAINING_DEVELOPMENT_PLAN.md`
- 可选：`README.md`

具体步骤：

1. 阅读 `README.md`、`docs/ARCHITECTURE.md`、`docs/AGENT_HARNESS.md`、`docs/TOOL_SECURITY.md`。
2. 将 `REMAINING_DEVELOPMENT_PLAN.md` 改成三段：
   - 当前已完成能力摘要。
   - 当前优先优化方向。
   - 后续新增能力路线。
3. 删除重复的旧阶段细节，但保留关键历史结论。
4. 在 README 的文档列表中加入本文件 `docs/NEXT_DEVELOPMENT_HANDOFF_PLAN.md`。

验收标准：

- 文档不再同时说“阶段 7 待做”和“阶段 7 已完成”。
- 后续模型能在 5 分钟内知道当前下一步。
- 没有代码改动。

验证命令：

```powershell
git diff --check
```

推荐提交信息：

```text
Clarify next development plan
```

## 任务 2：拆分 MainViewModel 的运行历史逻辑（✅ 已完成）

优先级：高。

目标：先降低 `MainViewModel.cs` 的维护风险。

背景：`src/AIChat.App/ViewModels/MainViewModel.cs` 已超过 3400 行。它包含设置、消息发送、Agent 运行、历史筛选、复制复盘包、工具审批等多种职责。不要一次性大拆。先拆最容易独立、风险最低的纯逻辑。

建议新增文件：

- `src/AIChat.App/ViewModels/AgentRunHistoryFilter.cs`
- `src/AIChat.App/ViewModels/AgentRunHistoryService.cs`

可能涉及文件：

- `src/AIChat.App/ViewModels/MainViewModel.cs`
- `src/AIChat.App/ViewModels/AgentRunHistoryItemViewModel.cs`
- `tests/AIChat.Tests` 当前没有 App 层测试，可先不新增测试项目；如果只抽纯逻辑，可考虑放 Application 层再测。

具体步骤：

1. 在 `MainViewModel.cs` 中搜索历史筛选相关成员，例如 `AgentRunHistory`、`SelectedAgentRunHistoryFilter`、`RefreshAgentRunHistory`、`CanRetry`、`CanContinue`。
2. 不要改 UI 绑定名称。先保留 MainViewModel 的公开属性和命令。
3. 将“根据状态筛选 AgentRun 列表”的逻辑抽到一个小类。
4. MainViewModel 只负责调用小类并更新 ObservableCollection。
5. 保证 XAML 无需大改。

验收标准：

- `MainViewModel.cs` 行数减少。
- 历史筛选行为不变。
- 运行历史的“全部、可重试、失败/停止、已完成、运行中”仍可用。
- 不引入新的线程或异步复杂度。

验证命令：

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
```

推荐提交信息：

```text
Extract agent run history filtering
```

常见错误：

- 不要把 WPF `ObservableCollection` 传入 Application 层。
- 不要修改 `AgentRun` 领域模型，只做 UI 层整理。
- 不要顺手改 XAML 样式。

## 任务 3：拆分 MainViewModel 的复盘包生成逻辑（✅ 已完成）

优先级：高。

目标：把复制复盘包、运行摘要文本生成从 UI 巨类中拿出来。

建议新增文件：

- `src/AIChat.App/ViewModels/AgentRunReviewPacketBuilder.cs`

可能涉及文件：

- `src/AIChat.App/ViewModels/MainViewModel.cs`
- `src/AIChat.App/ViewModels/AgentRunViewModel.cs`

具体步骤：

1. 在 `MainViewModel.cs` 中搜索 `ReviewPacket`、`Copy`、`Recovery`、`Summary`。
2. 找到生成复盘包文本的代码。
3. 将纯文本拼接逻辑移动到 `AgentRunReviewPacketBuilder`。
4. MainViewModel 保留命令和剪贴板调用。
5. 若 `AgentRunViewModel` 已经有相关属性，优先复用，不要重复计算复杂状态。

验收标准：

- 复制出的复盘包内容与之前等价或更清晰。
- MainViewModel 不再直接包含大段字符串拼接。
- 失败、取消、验证失败、成功运行都能生成复盘包。

验证命令：

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
```

推荐提交信息：

```text
Extract agent review packet builder
```

## 任务 4：新增 Agent Run 审计 Tab（✅ 已完成）

优先级：中高。

目标：提升可观测性。用户查看一个 Agent Run 时，能看到相关审计事件数量和关键事件列表。

背景：审计日志已经存在，但 UI 当前详情 Tab 主要有总览、计划、文件变更、验证。缺少审计视角。

建议涉及文件：

- `src/AIChat.App/MainWindow.xaml`
- `src/AIChat.App/ViewModels/AgentRunViewModel.cs`
- `src/AIChat.App/ViewModels/MainViewModel.cs`
- `src/AIChat.Storage.Json/AuditLogRepository.cs`
- `src/AIChat.Domain/Audit/AuditEvent.cs`

建议新增文件：

- `src/AIChat.App/ViewModels/AuditEventViewModel.cs`

具体步骤：

1. 先读 `AuditLogRepository`，确认查询 API。
2. 先做只读展示，不做过滤和导出。
3. 在选中某个 Agent Run 时，按 ProjectId 和时间范围查询相关 AuditEvent。
4. 若 AuditEvent 中没有 RunId，就用 AgentRun 的 StartedAt/CompletedAt 时间窗口近似匹配。
5. 在 Agent Run 详情 Tab 中加入“审计”。
6. 显示字段建议：时间、事件类型、工具名、结果摘要。

验收标准：

- 没有审计事件时显示简短空状态。
- 有事件时按时间升序或降序稳定显示。
- 不阻塞 UI；查询失败时不崩溃。
- 不改变审计日志写入格式。

验证命令：

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
```

推荐提交信息：

```text
Show audit events in agent run details
```

常见错误：

- 不要在 XAML 里放大段说明文字。
- 不要把审计事件全部一次性塞进复盘包。
- 不要为了 UI 改坏 JSONL 兼容性。

## 任务 5：补 Anthropic tool use 解析测试（✅ 已完成）

优先级：中高。

目标：降低 Provider 协议兼容风险。

背景：OpenAI-compatible 工具调用测试比较充分，Anthropic Provider 需要类似的协议样例覆盖。

建议涉及文件：

- `src/AIChat.Providers.Anthropic/AnthropicChatProvider.cs`
- `tests/AIChat.Tests/Providers`

建议新增文件：

- `tests/AIChat.Tests/Providers/AnthropicToolCallTests.cs`

具体步骤：

1. 阅读 `tests/AIChat.Tests/Providers/OpenAICompatibleToolCallTests.cs`，照它的测试风格写。
2. 阅读 `AnthropicChatProvider.cs`，找出解析 tool use 的方法。
3. 覆盖以下场景：
   - 单个 tool_use。
   - 多个 tool_use。
   - 普通 text 与 tool_use 混合。
   - tool input 为空对象。
   - tool input 不是对象或 JSON 不合法时不会崩溃。
4. 只改测试，除非测试暴露真实 bug。

验收标准：

- Anthropic Provider 解析工具调用的关键分支有测试。
- 异常格式不会导致整个响应解析崩溃。
- 不改变公开接口。

验证命令：

```powershell
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore --filter Anthropic
dotnet test AIChat.sln --no-restore
```

推荐提交信息：

```text
Cover Anthropic tool call parsing
```

## 任务 6：优化上下文包摘要（✅ 已完成）

优先级：中。

目标：让模型更容易理解当前项目，不盲目读文件。

建议涉及文件：

- `src/AIChat.Application/Context/ProjectContextPackBuilder.cs`
- `src/AIChat.Application/Workspace/ProjectFileIndexBuilder.cs`
- `tests/AIChat.Tests/Context/ProjectContextPackBuilderTests.cs`

具体步骤：

1. 阅读现有 `ProjectContextPackBuilderTests`。
2. 检查上下文包是否包含：
   - 项目根目录。
   - 文件类型统计。
   - source/test/config/doc 数量。
   - 最近或重要文件。
   - pinned context。
3. 如果缺少文件类型统计，新增统计摘要。
4. 保持预算裁剪逻辑，不要把完整大文件放进 prompt。
5. 增加测试覆盖预算内和超预算场景。

验收标准：

- 上下文包更短、更结构化。
- token/字符预算仍被遵守。
- 测试覆盖裁剪行为。

验证命令：

```powershell
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore --filter ProjectContextPackBuilderTests
dotnet test AIChat.sln --no-restore
```

推荐提交信息：

```text
Summarize project context file types
```

## 任务 7：补工具审批和审计的一致性测试（✅ 已完成）

优先级：中。

目标：确保工具调用、审批、拒绝、执行结果和审计事件一致。

建议涉及文件：

- `src/AIChat.Application/Agents/AgentHarness.cs`
- `src/AIChat.Application/Tools/ToolExecutionService.cs`
- `tests/AIChat.Tests/Agents/AgentHarnessTests.cs`
- `tests/AIChat.Tests/Tools/ToolExecutionServiceTests.cs`
- `tests/AIChat.Tests/Audit/AuditLogRepositoryTests.cs`

具体步骤：

1. 不急着改实现，先补测试。
2. 覆盖以下场景：
   - 工具被拒绝时，AgentRun 记录拒绝统计。
   - 工具被拒绝时，审计日志有拒绝事件。
   - Allow for Session 后，同工具第二次不再请求审批。
   - Disabled 工具不暴露给模型。
3. 如果现有测试已有部分覆盖，只补缺口。

验收标准：

- 审批行为和审计行为在测试中被锁住。
- 没有降低默认安全策略。

验证命令：

```powershell
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore --filter "AgentHarnessTests|ToolExecutionServiceTests|AuditLogRepositoryTests"
dotnet test AIChat.sln --no-restore
```

推荐提交信息：

```text
Cover approval audit consistency
```

## 任务 8：版本和发布体验打磨（✅ 已完成）

优先级：中低。

目标：让用户可以确认当前版本，并能按文档发布。

建议涉及文件：

- `src/AIChat.App/AIChat.App.csproj`
- `src/AIChat.App/ViewModels/MainViewModel.cs`
- `src/AIChat.App/MainWindow.xaml`
- `README.md`

具体步骤：

1. 确认窗口标题已经显示版本号。
2. 增加设置或关于区域中的版本显示，如果已有就只整理文档。
3. 在 README 中确认发布命令可用。
4. 可选：补充框架依赖发布和自包含发布的区别。

验收标准：

- 用户打开应用能看到版本。
- README 中的发布命令与 csproj 一致。
- 不引入安装器、自动更新等大功能。

验证命令：

```powershell
dotnet build AIChat.sln --no-restore
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained false
```

推荐提交信息：

```text
Polish desktop version and publish docs
```

## 额外完成的工作

以下工作在原始 8 个任务之外完成：

- **Anthropic 工具调用完整闭环**：provider 请求中携带 `tools` 字段（name/description/input_schema），响应解析 `tool_use` content blocks，多轮消息正确映射 `tool_use` 和 `tool_result` content blocks。
- **审计 RunId 过滤**：`AuditLogRepository.QueryAsync` 新增 `runId` 参数，支持服务端过滤，避免客户端 project id/path 不一致。
- **MainViewModel 进一步拆分**：`AgentRunHistoryFilter.GatherFromProject` 抽取数据采集逻辑；`AgentRunAuditLoader.LoadAsync` 抽取审计查询-排序管线（放在 `AIChat.Application.Audit`，有独立单测）；`WorkspaceChangeGrouper.Group` 抽取工作区变更分组逻辑到 Application 层（有独立单测），App 层 `WorkspaceChangeListBuilder` 只做 ViewModel 映射；`WorkspaceDiffFormatter` 抽取 diff 显示决策和格式化逻辑到 Application 层（有独立单测）；`AuditEventRecorder.RecordAsync` 抽取审计写入逻辑到 Application 层（有独立单测）；`WorkspaceOperationTextFormatter` 抽取工作区恢复/提交文案到 Application 层（有独立单测）；`WorkspaceRestoreBatchRunner.RestoreAsync` 抽取批量恢复循环到 Application 层（有 5 个 Moq 单测）；`IWorkspaceChangeService` 接口抽出用于可测试性。
- **上下文包文件类型统计**：`ProjectContextPackBuilder` 输出扩展名分布摘要（如 `.cs: 42, .json: 15`）。
- **Anthropic 消息归一化**：连续 `ChatRole.Tool` 合并为单个 user message 的多个 `tool_result` blocks，符合 Anthropic API 要求。
- **Provider JSON 容错**：`ParseJsonSafe` 对坏 `ArgumentsJson` 回退 `{}` 而非抛异常；tool 定义的 `parameters`/`input_schema` 在坏 JSON 时回退到 `{"type":"object","properties":{}}`，并有 payload 级测试锁定。

## 暂时不要做的事

低能力模型暂时不要碰这些：

- 完整 MCP 客户端实现。
- 完整 A2A 服务端实现。
- 多 Agent 并发队列。
- 自动更新器和安装包。
- 大规模 UI 重设计。
- 替换存储格式。
- 重写 Agent Harness。
- 让 shell 工具默认自动执行更多危险命令。

原因：这些任务涉及跨层设计、安全边界和兼容性，很容易把现有稳定基础破坏掉。

## 每个任务开始前的固定检查

```powershell
git status --short
dotnet test AIChat.sln --no-restore
```

如果测试本来就失败，先记录失败，不要把失败归因到自己的修改。

## 每个任务结束前的固定检查

代码改动：

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
git diff --check
git status --short
```

纯文档改动：

```powershell
git diff --check
git status --short
```

## 交付模板

每次完成一个任务后，按下面格式回复：

```text
完成内容：
- ...

修改文件：
- ...

验证：
- dotnet build AIChat.sln --no-restore
- dotnet test AIChat.sln --no-restore

风险/注意：
- ...

下一步建议：
- ...
```

## 给低能力模型的具体提示词模板

可以把下面模板直接交给模型：

```text
你在 D:\Code\AIChat 仓库中工作。请先阅读 README.md、docs/ARCHITECTURE.md、docs/AGENT_HARNESS.md、docs/TOOL_SECURITY.md、docs/NEXT_DEVELOPMENT_HANDOFF_PLAN.md。

只执行 docs/NEXT_DEVELOPMENT_HANDOFF_PLAN.md 中的“任务 X”。不要做其他任务。开始前运行 git status --short。不要删除或回滚用户未提交改动。

改动必须保持现有分层。不要扩大 MainViewModel；如果需要新增核心逻辑，优先放到独立类中。

完成后运行文档指定的验证命令，并用交付模板汇报。
```

