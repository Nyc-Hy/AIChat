# AIChat 剩余阶段开发计划

本文档用于把 AIChat 从当前的 ClaudeCode Desktop 雏形，继续推进到更完整、可恢复、可审计、可扩展的本地代码 Agent 桌面应用。

当前状态：第 1 到第 6 阶段已经完成。项目已经有 WPF 桌面壳、项目级会话、Provider 抽象、工具调用、项目路径保护、Agent Harness、运行历史、工作区快照、工具预算、审批护栏、结束校验、恢复建议和复盘包。

建议后续模型按本文档逐阶段执行。每个阶段尽量拆成小提交，每个提交都运行：

```powershell
dotnet build AIChat.sln
dotnet test AIChat.sln --no-build
```

如果只改文档，可以不跑测试，但提交前仍建议检查：

```powershell
git status --short
git diff --check
```

## 开发原则

1. 优先保持现有分层：UI 在 `AIChat.App`，领域模型在 `AIChat.Domain`，Agent 编排和工具逻辑在 `AIChat.Application`，Provider 协议适配在 `AIChat.Providers.*`。
2. 不要把更多 Agent 逻辑塞进 `MainViewModel`。新的核心能力优先放到 `AIChat.Application`，UI 只负责展示和命令绑定。
3. 每次新增 Agent 能力都要同时考虑四件事：可观测、可恢复、可权限控制、可测试。
4. 工具默认保守。读操作可以自动，写文件、改文件、shell 命令必须遵守现有权限模式和路径保护。
5. 不要一次性大重构。每阶段以能工作的垂直切片为主。

## 已完成阶段摘要

### 第 1 阶段：项目底座与 Git 化

已完成内容：

- 初始化 Git 仓库。
- 梳理项目结构和 README。
- 确认 WPF 桌面应用、Domain、Application、Provider、Storage、Tests 分层。

### 第 2 阶段：Agent 基础工具与路径安全

已完成内容：

- 建立 Agent Tool 抽象。
- 实现项目级工具：`list_files`、`read_file`、`search_text`、`write_file`、`edit_file`、`run_shell`。
- 加入 `ProjectPathGuard`，限制工具只能访问当前项目路径。
- 对危险目录和破坏性 shell 命令做基础阻断。

### 第 3 阶段：Agent 循环与模型工具调用

已完成内容：

- `AgentRunner` 支持把工具 schema 暴露给模型。
- 支持模型发起 tool call、应用执行工具、工具结果回填 transcript。
- 支持工具循环上限，避免无限工具调用。
- 支持流式普通文本输出。

### 第 4 阶段：运行可观测性与工作区快照

已完成内容：

- 引入 `AgentRun`、`AgentStep`、文件变更、验证记录。
- UI 显示 Agent Run 详情。
- 记录运行环境：项目路径、模型、启用工具、权限模式。
- 记录启动前工作区分支、未提交变更数量和预检信息。

### 第 5 阶段：Harness 护栏

已完成内容：

- `AgentHarness` 统一包裹 Agent 运行。
- 支持工具预算配置和预算耗尽事件。
- 支持审批统计：需要确认、拒绝、本会话允许。
- 支持识别“需要修改项目”的目标，并在没有写工具成功时标记护栏风险。
- 支持结束校验摘要。

### 第 6 阶段：运行恢复与复盘

已完成内容：

- 运行失败、取消、预算耗尽、审批拒绝、验证失败后生成恢复建议。
- Agent Run 详情可复制摘要和复盘包。
- 历史运行支持按全部、可重试、失败/停止、已完成、运行中筛选。

### 第 7 阶段：任务计划与检查清单（已完成）

- AgentPlan / AgentPlanItem 领域模型。
- 从模型输出中提取计划文本。
- UI 展示计划清单。

### 第 8 阶段：可暂停、可恢复、可继续的运行队列（已完成）

- AgentRun 持久化增强（ResumeTranscript、ContinuedFromRunId）。
- "继续运行" 和 "重试" 入口。
- 运行队列基础。

### 第 9 阶段：上下文工程与项目索引（已完成）

- ProjectFileIndex / ProjectFileIndexBuilder 扫描目录并分类文件。
- ProjectContextPackBuilder 组装预算化上下文包。
- PinnedContextItem 领域模型与 UI 绑定。

### 第 10 阶段：Patch、Diff 与变更控制（已完成）

- AgentFileChange 增加 ContentSnapshot 和 PostChangeHash。
- WriteFileTool / EditFileTool / ApplyPatchTool 记录快照和哈希。
- 回滚时检测冲突（当前文件哈希与 PostChangeHash 不一致）。

### 第 11 阶段：验证系统与自动修复循环（已完成）

- ProjectVerificationCommand 领域模型。
- VerificationResultParser 提取错误/警告摘要。
- AgentHarness.RunAutoVerifyLoopAsync 自动修复循环。

### 第 12 阶段：Provider 能力矩阵与模型兼容性（已完成）

- SendAsync 按 SupportsTools 能力门控，不支持时回退到普通聊天。
- ActiveModelSupportsTools 属性与 UI 警告条。
- OpenAICompatibleToolCallTests 覆盖多种 tool call 解析场景。

### 第 13 阶段：安全、权限与审计日志（已完成）

- ProjectWorkspace.ProjectToolPermissionModes 存储项目级工具权限覆盖。
- AuditLogRepository 以 JSONL 格式追加审计事件，支持按项目/类型/时间过滤。
- ShellCommandTool 扩展阻断列表和命令白名单。

### 第 14 阶段：插件化工具与 MCP/A2A 预留（已完成）

- AgentToolRegistry 替代 AgentToolCatalog，提供工具元数据。
- IExternalToolProvider 接口支持未来 MCP/A2A 工具注册。
- A2A_ADAPTER_DESIGN.md 设计文档。

### 第 15 阶段：桌面体验打磨（已完成）

- 合作器区域新增 Agent 状态栏，显示当前阶段、当前工具、工具预算等信息。
- Agent 运行详情面板重组为 Tab（总览、计划、文件变更、验证）。
- 合作器底部新增警告徽章：缺少 API Key、模型不支持工具。

目标：让 Agent 在开始动手前形成可见计划，并在执行过程中更新任务状态。这一阶段会让 AIChat 更接近 ClaudeCode Desktop 的“工作流感”，用户能看到它准备做什么、正在做什么、还剩什么。

### 7.1 新增领域模型

建议新增文件：

```text
src/AIChat.Domain/Chat/AgentPlan.cs
src/AIChat.Domain/Chat/AgentPlanItem.cs
```

建议字段：

- `AgentPlan.Id`
- `AgentPlan.RunId`
- `AgentPlan.CreatedAt`
- `AgentPlan.UpdatedAt`
- `AgentPlan.Summary`
- `AgentPlan.Items`
- `AgentPlanItem.Id`
- `AgentPlanItem.Title`
- `AgentPlanItem.Status`
- `AgentPlanItem.Notes`
- `AgentPlanItem.Order`

建议状态：

```csharp
public enum AgentPlanItemStatus
{
    Pending,
    InProgress,
    Completed,
    Blocked,
    Skipped
}
```

验收标准：

- `AgentRun` 能持有一个可选 `AgentPlan`。
- JSON 存储可以正常序列化和反序列化旧数据，新字段缺失时不崩溃。

推荐提交：

```text
Add agent plan domain model
```

### 7.2 从模型输出中提取计划

建议位置：

```text
src/AIChat.Application/Agents/AgentHarness.cs
src/AIChat.Application/Prompting/SystemPromptBuilder.cs
```

实现思路：

1. 在系统提示词中要求 Agent 对复杂任务先输出简短计划。
2. 对模型普通文本不要做脆弱的自然语言解析。
3. 更稳妥的做法是新增一个内部工具，例如 `update_plan`，让模型通过工具结构化更新计划。
4. `update_plan` 不做文件系统副作用，只更新当前 `AgentRun.Plan`。

建议工具 schema：

```json
{
  "summary": "本轮任务计划摘要",
  "items": [
    {
      "title": "阅读现有 Harness 代码",
      "status": "completed",
      "notes": "已确认入口在 AgentHarness"
    }
  ]
}
```

注意事项：

- `update_plan` 是 harness 内置工具，不应该暴露为普通项目工具。
- 计划工具执行后要产生 `AgentStep`，但不要计入危险工具。
- 如果模型不调用计划工具，也不要阻止普通聊天。计划是增强能力，不是所有请求的硬性前置。

验收标准：

- 模型可以通过结构化方式创建或更新计划。
- Agent Run 详情能显示计划摘要和任务状态。
- 单元测试覆盖计划更新参数解析、状态映射、旧计划更新。

推荐提交：

```text
Support structured agent plan updates
```

### 7.3 UI 展示计划

建议文件：

```text
src/AIChat.App/ViewModels/AgentPlanViewModel.cs
src/AIChat.App/ViewModels/AgentPlanItemViewModel.cs
src/AIChat.App/ViewModels/AgentRunViewModel.cs
src/AIChat.App/MainWindow.xaml
```

实现内容：

- 在 Agent Run 详情中新增“计划”区块。
- 用列表展示计划项：待办、进行中、完成、阻塞、跳过。
- `RunSummary` 和 `ReviewPacket` 中加入计划摘要。
- 计划变化时触发 `OnPropertyChanged`，避免详情面板显示旧状态。

验收标准：

- 有计划时显示计划，没有计划时不占用大量空间。
- 复制复盘包时包含计划状态。
- 构建通过。

推荐提交：

```text
Show agent plans in run details
```

## 第 8 阶段：可暂停、可恢复、可继续的运行队列

目标：把当前“单次运行”升级为“可管理运行”。用户可以停止、查看、复制恢复建议，也可以从历史运行继续执行。后续如果要做长任务，这是必要基础。

### 8.1 运行状态持久化增强

建议改造：

```text
src/AIChat.Domain/Chat/AgentRun.cs
src/AIChat.Storage.Json/JsonAppRepository.cs
```

实现内容：

- 确认 `AgentRun` 的所有关键字段都能持久化：计划、步骤、文件变更、验证、恢复建议、结束校验、预算状态。
- 给 `AgentRun` 增加 `CanResume` 或在 ViewModel 中计算。
- 运行未完成时应用关闭，下次打开能看到“中断运行”。

验收标准：

- 保存并重启应用后，运行历史仍能看到完整 Agent Run。
- 失败、取消、中断运行都能复制复盘包。

推荐提交：

```text
Persist resumable agent run state
```

### 8.2 继续运行入口

建议文件：

```text
src/AIChat.App/ViewModels/MainViewModel.cs
src/AIChat.App/MainWindow.xaml
```

实现内容：

- 当前“重试”是把恢复建议放回输入框。
- 新增“继续”命令，自动用恢复建议作为新用户消息发送。
- 继续运行时应关联原运行 ID，例如在新 `AgentRun` 中记录 `ContinuedFromRunId`。

建议字段：

```csharp
public string ContinuedFromRunId { get; set; } = "";
```

验收标准：

- 历史详情中失败运行可以点击“继续”。
- 新运行的复盘包能说明它是从哪个 Run 继续的。
- 如果当前正在发送，继续按钮禁用。

推荐提交：

```text
Add continue action for recoverable agent runs
```

### 8.3 运行队列基础

建议新增：

```text
src/AIChat.Application/Agents/AgentRunQueue.cs
```

第一版不要做复杂并发。只需要：

- 同一时间只允许一个 Agent Run 执行。
- 如果用户发起第二个 Agent 任务，提示当前已有任务运行。
- 队列类负责暴露 `IsRunning`、`CurrentRunId`。

验收标准：

- UI 仍然不会同时启动两个 Agent 循环。
- 测试覆盖重复启动时返回明确结果。

推荐提交：

```text
Introduce single-run agent queue
```

## 第 9 阶段：上下文工程与项目索引

目标：减少模型盲读文件，提高上下文命中率。ClaudeCode 类工具的核心不是“能读文件”，而是“知道先读哪些文件、如何压缩读过的内容”。

### 9.1 项目文件索引

建议新增：

```text
src/AIChat.Application/Workspace/ProjectFileIndex.cs
src/AIChat.Application/Workspace/ProjectFileIndexBuilder.cs
```

索引内容：

- 相对路径
- 文件大小
- 最近修改时间
- 扩展名
- 是否被忽略
- 简短类型标签，例如 source、test、config、doc、asset

忽略规则：

- `.git`
- `.vs`
- `bin`
- `obj`
- `artifacts`
- `TestResults`
- `node_modules`
- 常见大文件和二进制文件

验收标准：

- 能为当前项目生成轻量索引。
- 大目录不会卡死 UI。
- 单元测试覆盖忽略规则和路径归一化。

推荐提交：

```text
Build lightweight project file index
```

### 9.2 上下文包生成器

建议新增：

```text
src/AIChat.Application/Context/ProjectContextPackBuilder.cs
```

上下文包应包含：

- 项目根目录
- 当前分支和工作区摘要
- 文件索引摘要
- 最近 Agent Run 摘要
- 用户当前会话最近消息
- 已引用文件片段

实现要求：

- 明确 token/字符预算。
- 超预算时按优先级裁剪。
- 不要把大文件完整塞入 prompt。

验收标准：

- `AgentHarness` 可以把上下文包放进系统消息或开发者消息。
- 单元测试覆盖预算裁剪。

推荐提交：

```text
Assemble budgeted project context packs
```

### 9.3 文件引用与 pinned context

建议 UI 能力：

- 用户可以把某个文件或文件片段固定到上下文。
- Agent 运行时优先带上 pinned 文件。

建议模型：

```text
src/AIChat.Domain/Context/PinnedContextItem.cs
```

字段：

- `ProjectId`
- `Path`
- `StartLine`
- `EndLine`
- `Note`
- `CreatedAt`

验收标准：

- 可以保存 pinned context。
- 上下文包会包含 pinned context。
- UI 有最小入口查看和移除 pinned item。

推荐提交：

```text
Support pinned project context
```

## 第 10 阶段：Patch、Diff 与变更控制

目标：让写文件和改文件变得更可审计。现在已有文件变更记录和工作区工具，但还需要更像代码 Agent 的“看 diff、应用 patch、回滚本轮”的体验。

### 10.1 统一变更记录

建议改造：

```text
src/AIChat.Domain/Chat/AgentFileChange.cs
src/AIChat.Application/Workspace/WorkspaceChangeService.cs
```

实现内容：

- 记录变更前摘要和变更后摘要。
- 记录工具名、步骤号、时间。
- 对 `write_file`、`edit_file`、`run_shell` 造成的文件变化做归因。

验收标准：

- Agent Run 详情能说明每个文件由哪个步骤改变。
- 复盘包包含变更归因。

推荐提交：

```text
Attribute file changes to agent steps
```

### 10.2 Patch 工具

建议新增工具：

```text
apply_patch
```

注意：这个工具不应该直接暴露 PowerShell 字符串拼接。应接收结构化参数：

```json
{
  "path": "src/example.cs",
  "find": "old text",
  "replace": "new text"
}
```

或使用受控 unified diff。第一版建议先做 find/replace，并要求唯一匹配。

验收标准：

- 匹配 0 次或多次时拒绝修改。
- 修改前后记录 diff。
- 测试覆盖路径逃逸、重复匹配、成功替换。

推荐提交：

```text
Add guarded patch editing tool
```

### 10.3 本轮变更回滚

实现内容：

- 对 Agent 修改前的文件内容做快照。
- 提供“回滚本轮变更”按钮。
- 只回滚本轮 Agent 触碰的文件，不碰用户后来手动改的文件。

关键风险：

- 如果用户在 Agent 修改后又手动改了同一文件，不能无提示覆盖。
- 可以通过比较当前文件 hash 和 Agent 修改后 hash 判断是否安全回滚。

验收标准：

- 安全时一键回滚。
- 有冲突时提示用户，不自动覆盖。
- 测试覆盖 hash 判断。

推荐提交：

```text
Restore agent run file snapshots safely
```

## 第 11 阶段：验证系统与自动修复循环

目标：让 Agent 不只是改文件，还能运行验证、理解失败、继续修复。

### 11.1 验证命令配置

建议模型：

```text
src/AIChat.Domain/Projects/ProjectVerificationCommand.cs
```

字段：

- `Name`
- `Command`
- `WorkingDirectory`
- `TimeoutSeconds`
- `IsDefault`

UI：

- 设置或项目面板中配置验证命令。
- 默认可识别 `.sln` 项目，提供 `dotnet build` 和 `dotnet test`。

验收标准：

- 每个项目可保存验证命令。
- Agent 可以读取默认验证命令。

推荐提交：

```text
Configure project verification commands
```

### 11.2 验证结果解析

建议新增：

```text
src/AIChat.Application/Verification/VerificationResultParser.cs
```

第一版目标：

- 识别 exit code。
- 抽取失败命令。
- 抽取关键错误行。
- 限制输出长度，避免把完整日志塞进上下文。

验收标准：

- Agent Run 的验证记录包含摘要和关键日志。
- 复盘包包含失败验证摘要。

推荐提交：

```text
Summarize verification command output
```

### 11.3 自动修复循环

实现位置：

```text
src/AIChat.Application/Agents/AgentHarness.cs
```

行为：

1. Agent 完成修改后，运行默认验证。
2. 如果验证失败，把摘要作为工具结果回填给模型。
3. 允许模型继续修复。
4. 受工具预算和修复次数预算控制。

新增设置：

- `AutoVerifyAgentRuns`
- `MaxAutoFixRounds`

验收标准：

- 修改类任务能自动跑验证。
- 验证失败会进入恢复建议。
- 不会无限修复。

推荐提交：

```text
Run bounded auto-fix verification loops
```

## 第 12 阶段：Provider 能力矩阵与模型兼容性

目标：不同模型对 tool call、thinking、JSON、流式协议支持不一样。AIChat 需要用能力矩阵驱动 UI 和 Harness 决策。

### 12.1 能力矩阵收敛

建议检查：

```text
src/AIChat.Abstractions/Configuration/LlmProviderInfo.cs
src/AIChat.Application/Llm/Routing/ChatProviderCatalog.cs
```

实现内容：

- 明确每个模型是否支持 tools。
- 明确是否支持 streaming。
- 明确是否支持 reasoning/thinking 参数。
- 明确 tool call 格式类型。

验收标准：

- 不支持 tools 的模型不会进入 Agent 模式，或 UI 明确提示。
- 设置界面只显示当前模型支持的参数。

推荐提交：

```text
Gate agent mode by model capabilities
```

### 12.2 Provider tool call 兼容测试

测试重点：

- OpenAI-compatible tool call。
- Anthropic tool use。
- 空 tool calls。
- 多 tool calls。
- tool call JSON 参数不合法。

验收标准：

- Provider 层解析不会因为某家模型格式略有差异而崩溃。
- 测试覆盖关键响应样例。

推荐提交：

```text
Cover provider tool call parsing variants
```

> ✅ 第 12 阶段已完成（commit `db17d2d`）。
>
> 已实现：
> - `MainViewModel.SendAsync` 在进入 Harness 前解析 `SupportsTools`，不支持时回退到普通聊天并设置状态提示。
> - 新增 `ActiveModelSupportsTools` 计算属性，供 UI 绑定。
> - 工具设置页新增黄色警告条，当模型不支持工具时可见。
> - 新增 `OpenAICompatibleToolCallTests` 覆盖单工具、多工具、空数组、流式拼接、缺省参数、ID 回退等场景。

## 第 13 阶段：安全、权限与审计日志

目标：让 AIChat 在本地代码项目里可放心使用。这里要做得保守，不要追求“全自动万能”。

### 13.1 权限配置持久化增强

实现内容：

- 每个工具单独权限。
- 每个项目可以覆盖全局工具权限。
- session approval 到会话结束或应用关闭后失效。

验收标准：

- UI 能清楚显示当前工具权限。
- 权限变更会持久化。
- session approval 不会永久保存成全局允许。

推荐提交：

```text
Persist project-scoped tool permissions
```

### 13.2 审计日志

建议新增：

```text
src/AIChat.Domain/Audit/AuditEvent.cs
src/AIChat.Storage.Json/AuditLogRepository.cs
```

记录事件：

- 工具调用请求。
- 用户批准或拒绝。
- 文件写入。
- shell 命令执行。
- 回滚操作。
- 自动验证。

验收标准：

- 审计日志可按项目查看。
- 复盘包可以引用审计事件数量。

推荐提交：

```text
Record audit events for agent actions
```

### 13.3 Shell 沙箱策略

当前 `run_shell` 已有基础阻断，后续要更系统：

- 命令 allowlist 模式。
- 默认允许 `dotnet build`、`dotnet test`、`git status`、`git diff`、`rg`。
- 高风险命令必须确认。
- 禁止递归删除、强制 reset、跨目录移动等危险操作。

验收标准：

- 单元测试覆盖常见危险命令。
- UI 审批弹窗显示命令、工作目录、风险说明。

推荐提交：

```text
Harden shell command policy
```

> ✅ 第 13 阶段已完成（commit `da89499`）。
>
> 已实现：
> - `ProjectWorkspace.ProjectToolPermissionModes` 存储项目级工具权限覆盖，合并时项目值优先。
> - UI 工具设置页新增"项目工具权限覆盖"区域，支持添加/删除覆盖项。
> - `AuditLogRepository` 以 JSONL 格式追加审计事件，支持按项目、类型、时间过滤。
> - AgentHarness 事件循环中记录工具调用、拒绝、运行开始/完成等审计事件。
> - `ShellCommandTool` 扩展阻断列表（`-rf`、`--force`、`git push --force` 等），新增命令白名单（`dotnet build/test`、`git status/diff`、`rg` 等）。
> - 36 个 Shell 命令策略测试覆盖白名单和危险命令检测。

## 第 14 阶段：插件化工具与 MCP/A2A 预留

目标：不要一开始就把架构绑死在某个概念上。建议先做“本地插件化工具接口”，再预留 MCP/A2A 适配层。A2A 和认知混合框架可以作为上层编排，不应该替代当前 Harness。

### 14.1 工具注册中心

建议新增：

```text
src/AIChat.Application/Tools/AgentToolRegistry.cs
```

实现内容：

- 根据配置启用工具。
- 支持内置工具和未来外部工具。
- 提供工具元数据：危险等级、权限默认值、分类、说明。

验收标准：

- `AgentHarness` 不直接硬编码工具列表。
- 设置 UI 从 registry 读取工具展示。

推荐提交：

```text
Introduce agent tool registry metadata
```

### 14.2 MCP 适配预留

建议新增接口：

```csharp
public interface IExternalToolProvider
{
    string Id { get; }
    Task<IReadOnlyList<IAgentTool>> GetToolsAsync(CancellationToken cancellationToken);
}
```

第一版只做接口和空实现，不急着接真实 MCP。

验收标准：

- 内置工具仍然工作。
- 未来可新增 `McpToolProvider`，不需要重写 Harness。

推荐提交：

```text
Prepare external tool provider abstraction
```

### 14.3 A2A 适配层设计

建议只写设计文档，不马上实现完整 A2A。

文档应说明：

- AIChat 本地 Agent 是一个 Agent。
- 外部 Agent 可以通过协议请求 AIChat 执行项目级任务。
- 所有外部请求仍经过 Harness、权限、审计和工作区保护。
- A2A 不直接绕过工具权限。

推荐文档：

```text
docs/A2A_ADAPTER_DESIGN.md
```

推荐提交：

```text
Document future A2A adapter boundary
```

> ✅ 第 14 阶段已完成（commit `4d8fe29`）。
>
> 已实现：
> - `AgentToolRegistry` 替代 `AgentToolCatalog`，提供工具元数据（分类、默认权限、分组标签）。
> - `IExternalToolProvider` 接口支持未来 MCP/A2A 工具注册。
> - `docs/A2A_ADAPTER_DESIGN.md` 设计文档说明外部 Agent 如何通过 Harness 执行项目任务。

## 第 15 阶段：桌面体验打磨

目标：让这个工具真正像桌面产品，而不是测试壳。

### 15.1 Agent 状态栏

显示：

- 当前阶段。
- 当前工具。
- 工具预算剩余。
- 当前计划项。
- 是否等待用户审批。

验收标准：

- 用户不打开详情也能知道 Agent 在做什么。
- 审批等待状态明显。

推荐提交：

```text
Show live agent status in composer
```

### 15.2 详情面板信息架构整理

当前详情逐渐增多，后续建议改为 Tab：

- 总览
- 计划
- 步骤
- 文件变更
- 验证
- 审计
- 复盘

验收标准：

- 信息更易扫描。
- 不要把所有内容塞进一个长滚动面板。

推荐提交：

```text
Organize agent run details into tabs
```

### 15.3 空状态与错误状态

完善：

- 没有项目时。
- 模型不支持工具时。
- API Key 缺失时。
- 工具被禁用时。
- 权限被拒绝时。

验收标准：

- 用户知道为什么 Agent 没执行。
- 恢复建议和 UI 提示一致。

推荐提交：

```text
Clarify agent unavailable states
```

> ✅ 第 15 阶段已完成（commit `32bb81a`）。
>
> 已实现：
> - 合作器区域新增 Agent 状态栏，显示当前阶段、当前工具、工具预算等信息。
> - Agent 运行详情面板重组为 Tab（总览、计划、文件变更、验证）。
> - 合作器底部新增警告徽章：缺少 API Key、模型不支持工具。

## 第 16 阶段：文档、示例与发布准备

目标：让别人可以接手、运行、测试、理解架构。

### 16.1 更新 README

补充：

- Agent 模式说明。
- 工具权限说明。
- 运行历史和复盘包说明。
- 常见问题。

推荐提交：

```text
Document agent workflow in README
```

### 16.2 开发者架构文档

建议新增：

```text
docs/ARCHITECTURE.md
docs/AGENT_HARNESS.md
docs/TOOL_SECURITY.md
```

内容：

- 分层图。
- Agent Harness 生命周期。
- 工具权限模型。
- 恢复和复盘机制。

推荐提交：

```text
Add agent architecture documentation
```

### 16.3 打包与版本信息

实现内容：

- 应用版本显示。
- 发布配置说明。
- Windows 桌面发布命令。

命令示例：

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained false
```

验收标准：

- 能生成可运行发布目录。
- README 说明如何发布。

推荐提交：

```text
Document desktop publish workflow
```

> ✅ 第 16 阶段已完成（commit `TBD`）。
>
> 已实现：
> - README 重写，包含 Agent 模式、工具权限、运行历史、架构概览和发布说明。
> - 新增 `docs/ARCHITECTURE.md`、`docs/AGENT_HARNESS.md`、`docs/TOOL_SECURITY.md`。
> - `AIChat.App.csproj` 添加 Version、AssemblyVersion、FileVersion、Product、Description 属性。
> - 窗口标题绑定 `WindowTitle` 属性，显示版本号和项目名称。

## 推荐执行顺序

如果用更便宜的模型继续开发，建议按这个顺序推进：

1. 第 7 阶段：任务计划与检查清单。
2. 第 8 阶段：可暂停、可恢复、可继续的运行队列。
3. 第 9 阶段：上下文工程与项目索引。
4. 第 10 阶段：Patch、Diff 与变更控制。
5. 第 11 阶段：验证系统与自动修复循环。
6. 第 12 阶段：Provider 能力矩阵与模型兼容性。
7. 第 13 阶段：安全、权限与审计日志。
8. 第 14 阶段：插件化工具与 MCP/A2A 预留。
9. 第 15 阶段：桌面体验打磨。
10. 第 16 阶段：文档、示例与发布准备。

最重要的前三个后续阶段是 7、8、9。完成后，AIChat 会从“能执行工具的聊天应用”明显变成“有计划、有恢复、有项目上下文的代码 Agent 桌面工具”。

## 每次开发的交付模板

建议后续模型每做一个小任务，都按这个模板结束：

```text
完成内容：
- ...

修改文件：
- ...

验证：
- dotnet build AIChat.sln
- dotnet test AIChat.sln --no-build

提交：
- <commit hash> <commit message>

下一步建议：
- ...
```

## 给后续模型的注意事项

- 先读 `README.md`、`docs/CODE_EXPLAINED.md`、本文档，再改代码。
- 搜索代码优先用 `rg`。
- 手动编辑文件时保持变更小而清晰。
- 不要删除用户未提交变更。
- 不要绕过 `ProjectPathGuard`。
- 不要让 shell 工具默认执行高风险命令。
- 每个阶段至少补关键单元测试，尤其是路径、安全、预算、序列化、恢复逻辑。
- 如果遇到架构疑问，优先保持现有 Harness 路线。MCP、A2A、认知混合框架都应该作为可插拔层，不应该推翻当前 Harness。
