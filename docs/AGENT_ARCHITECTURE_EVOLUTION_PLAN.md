# Agent 架构演进计划

本文档描述 AIChat 从当前“单 Agent 线性工具循环”演进到“阶段化、可调度、上下文经济型 Agent 系统”的计划。

目标不是一次性重写 Agent，而是在现有 Harness、权限、审计、验证和持久化基础上逐步扩展。每一阶段都应保持可运行、可测试、可回滚。

## 1. 当前基线

AIChat 已具备稳定的本地代码 Agent 基座：

- `MainViewModel` 发起用户请求、更新 UI、处理审批并保存运行结果。
- `AgentRequestFactory` 构建 `ChatRequest`、`AgentRunContext` 和请求快照。
- `AgentHarness` 记录 `AgentRun`、步骤、文件变更、验证和计划。
- `AgentRunner` 执行模型/工具循环。
- `ToolExecutionService` 负责工具权限、审批和实际执行。
- `AgentRunAuditService` 记录审计事件。
- `JsonAppRepository` 持久化 settings、projects、conversations 和 runs。
- `ToolSettingsService`、`ProviderSettingsService`、`AdvancedSettingsService` 承担设置归一化。
- `ConversationContextBuilder` 构建 system prompt 和会话上下文。

当前主链路：

```text
User goal
 -> MainViewModel.SendAsync
 -> AgentRequestFactory
 -> ChatRequest + AgentRunContext
 -> AgentHarness
 -> AgentRunner
 -> IChatCompletionService
 -> ToolExecutionService
 -> tool result back into transcript
 -> repeat until final response
 -> optional auto verification / repair
 -> audit + persistence
```

## 2. 目标架构

```text
User input
 -> Intake / multimodal understanding
 -> Planner LLM produces structured plan
 -> Coordinator validates plan, budget, permissions, and risk
 -> Context Router retrieves minimal task context
 -> Prompt Composer generates model- and phase-specific prompts
 -> Agent Runtime executes approved steps
 -> Tool Layer executes tools with permission and audit
 -> Artifact Store records raw outputs and summaries
 -> Summarizer compresses findings/tool results
 -> Verifier validates changes
 -> Coordinator decides complete / continue / repair / ask user
```

核心原则：

1. LLM 决定语义层面的下一步。
2. 系统负责权限、预算、风险规则和调度策略。
3. 先路由上下文，再组合提示词，不默认塞入全部信息。
4. Agent 实例从预定义模板按需创建。
5. 工具结果应摘要化并通过产物引用，而不是盲目追加到上下文。
6. 长任务按阶段和检查点推进，不进入无边界循环。

## 3. 缺失能力

| 能力 | 当前状态 | 需要补齐 |
|---|---|---|
| Planner | 模型可能调用 `update_plan`，但没有系统规划阶段 | 执行前生成结构化计划 |
| Coordinator | `MainViewModel` 和 `AgentHarness` 运行单循环 | 系统级状态机和调度策略 |
| Prompt Composer | 多为静态 system prompt 和累积 transcript | 按阶段、模型和任务生成提示词 |
| Context Router | 有预算化会话上下文和文件索引 | 任务感知检索、引用和最近变更评分 |
| Tool Result Summarizer | 工具输出直接进入 transcript | 摘要 + 产物引用 + 按需展开 |
| Memory Layer | 会话历史和固定上下文 | 用户、项目、任务、工具结果记忆 |
| Agent Templates | 一个 `AgentRunner` 角色 | Planner、Explorer、Worker、Verifier、Summarizer、Reviewer 模板 |
| Sub-agent Runtime | 暂无 | 具备预算和写入范围的按需子 Agent |
| Budget Manager | 工具调用上限和自动修复上限 | Token、时间、工具、阶段和 Agent 预算 |
| Artifact Store | 调用详情、审计、项目持久化 | 原始输出、日志、diff 和摘要的结构化引用 |
| Multimodal Intake | 主要处理文本/代码 | 图片、文档、截图和提取摘要 |

## 4. 开发阶段

### 阶段 1：工具结果摘要和产物引用

目标：在增加更多编排前先减少 token 浪费。

交付内容：

- 增加 `AgentArtifact` 领域模型，包含 `Id`、`RunId`、`StepId`、`Kind`、`Summary`、`Content`、`CreatedAt` 和可选 metadata。
- 为 `AgentRun` 增加产物列表或仓储方法。
- 增加 `ToolResultSummarizer`，摘要搜索结果、文件读取、命令输出和 diff。
- 大型工具输出以“给模型的摘要 + UI 可查看的原始产物引用”形式传递。

验收标准：

- 大型工具输出默认不完整进入下一次 LLM 请求。
- UI 仍可展示或复制原始工具输出。
- 审计仍记录工具执行。
- 现有工具测试通过。
- 新增测试覆盖截断、摘要和引用行为。

建议文件：

- `src/AIChat.Domain/Chat/AgentArtifact.cs`
- `src/AIChat.Application/Agents/ToolResultSummarizer.cs`
- `src/AIChat.Application/Agents/AgentArtifactService.cs`
- `tests/AIChat.Tests/Agents/ToolResultSummarizerTests.cs`

### 阶段 2：结构化 Planner

目标：在执行前增加显式规划阶段。

交付内容：

- 增加 `AgentStructuredPlan`、`AgentPlanPhase`、`AgentPlanTask`、`AgentPlanRisk`、`AgentPlanBudget`。
- 增加 `PlannerPromptBuilder` 和 `AgentPlanner`。
- 解析并校验 Planner JSON：拒绝空计划、限制任务数量、归一化未知阶段、提取建议工具和上下文。
- 将结构化计划持久化到 `AgentRun.Plan` 或新字段。

验收标准：

- Agent 运行以校验后的结构化计划开始。
- Planner 输出坏 JSON 时回退到简单单阶段计划。
- UI 可以展示阶段和任务。
- 测试覆盖有效计划、无效计划回退、风险和预算归一化。

### 阶段 3：Coordinator 状态机

目标：把运行编排从隐式单循环变成显式阶段。

目标状态：

```text
Planning
GatheringContext
Executing
Verifying
Repairing
Summarizing
WaitingForUser
Completed
Failed
Cancelled
```

交付内容：

- 增加 `AgentCoordinator` 和 `AgentRunPhase`。
- 增加阶段切换事件。
- 在 `AgentRun` 中记录每个阶段的状态和摘要。
- 让 `AgentHarness` 将高层决策委托给 Coordinator，同时保留 `AgentRunner` 作为底层模型/工具循环。

验收标准：

- 现有单 Agent 行为保持功能等价。
- 运行详情可以展示当前阶段。
- 验证和修复被表示为明确阶段。
- 测试覆盖阶段切换和取消。

### 阶段 4：Prompt Composer

目标：用阶段感知提示词替代单一通用提示词。

交付内容：

- 增加 `AgentPromptComposer` 和 `AgentPromptProfile`。
- 输入包括阶段、任务目标、模型/Provider 信息、计划、上下文引用、记忆片段、允许工具和预算。
- 输出包括 system message、必要的 developer-style instructions、用户任务消息和结构化响应要求。
- 支持 planning、context gathering、execution、verification repair、summarization、review 等 profile。

验收标准：

- Planner、执行和修复提示词都通过 Composer 生成。
- 提示词大小可测量。
- 测试通过快照覆盖关键提示词片段，但不过度锁死具体措辞。

### 阶段 5：Context Router

目标：按任务和阶段路由最小必要上下文。

交付内容：

- 增加 `ContextRouter`。
- 增加任务感知相关性评分：路径/文件名匹配、最近编辑、固定上下文、会话提及、测试/源码配对、文件类型标签。
- 返回包含摘要、已包含文件、片段、产物引用、相关但省略引用和 token 估算的上下文包。
- 可行时增加增量索引更新路径。

验收标准：

- Context Router 能为具体任务返回小而准的上下文包。
- 不会盲目包含大文件。
- LLM 调用前可获得 token 估算。
- 测试覆盖评分和预算裁剪。

### 阶段 6：Memory Layer

目标：将长期记忆与临时 transcript 分离。

记忆类别：

- 用户记忆：偏好、风格、重复指令。
- 项目记忆：架构、约定、重要决定。
- 任务记忆：当前运行发现、假设、检查点。
- 工具记忆：工具输出摘要和引用。

验收标准：

- Planner 和 Context Router 可以检索项目/任务记忆。
- 记忆条目带来源和时间戳。
- 测试覆盖检索、过滤和禁止存储密钥策略。

### 阶段 7：Agent 模板

目标：在引入子 Agent 前定义角色模板。

模板包括：

- Planner：生成结构化计划。
- Explorer：只读代码库分析。
- Worker：在指定范围内修改。
- Verifier：运行检查并解释失败。
- Summarizer：压缩结果和产物。
- Reviewer：查找风险和缺失测试。

验收标准：

- 模板足够数据化/配置化，便于演进。
- Coordinator 可以选择模板，即使暂不生成子 Agent。
- 测试覆盖模板能力和默认权限。

### 阶段 8：Sub-agent Runtime

目标：允许 Coordinator 批准的子 Agent。

交付内容：

- 增加 `SubAgentRun`、`SubAgentScheduler` 和 `SubAgentResult`。
- 为子 Agent 提供隔离上下文：任务、最小上下文包、工具权限、写入范围和预算。
- 增加安全规则：同一未解决任务不重复创建 Agent；Worker 写入范围必须不重叠或显式串行；Verifier/Explorer 不能编辑；所有工具调用继续走权限和审计链路。

验收标准：

- Coordinator 可以运行一个只读 Explorer 子 Agent。
- 父运行能接收结构化结果。
- 审计能归属父运行和子 Agent。
- 测试覆盖预算、范围、取消和结果聚合。

### 阶段 9：预算管理和检查点

目标：支持长任务，同时保持可控。

预算类型：

- 工具调用数
- 模型 token
- 运行时间
- 阶段调用数
- 子 Agent 调用数
- 自动修复轮数
- 文件变更数量

验收标准：

- 长任务可以在预算检查点暂停。
- 用户可以追加预算继续。
- 现有最大工具轮数仍作为最终安全上限。
- 测试覆盖预算消耗和检查点触发。

### 阶段 10：多模态输入

目标：让图片、文档和截图以结构化产物进入规划/上下文系统。

交付内容：

- 增加 `InputArtifact` 模型。
- 增加输入产物提取流程：图片描述、OCR 文本、文档摘要、表格摘要、截图 UI 元素摘要。
- 让计划和上下文引用输入产物。
- 让 Prompt Composer 支持多模态摘要。

验收标准：

- 用户附加图片或文档后，Planner 能看到简洁摘要。
- 原始产物可检查。
- Planner 可以请求产物引用的更多细节。
- 测试覆盖纯文本回退和产物 metadata。

## 5. 推荐实现顺序

1. 工具结果摘要和产物引用。
2. 结构化 Planner。
3. Coordinator 状态机。
4. Prompt Composer。
5. Context Router。
6. Memory Layer。
7. Agent 模板。
8. Sub-agent Runtime。
9. 预算管理和检查点。
10. 多模态输入。

这个顺序的原因是：摘要/产物先解决最大的 token 浪费；Planner 和 Coordinator 应先于子 Agent；Prompt Composer 和 Context Router 会让后续每一次 LLM 调用更便宜、更清晰；Memory 和模板为子 Agent 铺路；子 Agent 较晚引入，因为它会放大权限、审计、预算和 UI 状态复杂度。

## 6. 横向要求

### 权限

所有工具，包括未来子 Agent 使用的工具，都必须继续经过：

- `ToolExecutionService`
- `ToolPermissionMode`
- 项目级覆盖
- 审批 UI
- 审计日志
- `ProjectPathGuard`

任何新路径都不能绕过这些层。

### 审计

每个阶段和子 Agent 都应产生可审计事件：

- 阶段开始/完成
- Planner 输出接受/拒绝
- 子 Agent 创建/完成
- 到达预算检查点
- 工具结果已摘要
- 产物已存储

### 持久化

长运行任务需要持久状态：

- 当前阶段
- 结构化计划
- 产物
- 任务记忆
- 预算消耗
- 子 Agent 结果
- 检查点决策

### UI

UI 应展示：

- 当前阶段
- 计划
- 活跃子 Agent 任务
- 预算使用
- 产物和摘要
- 验证状态
- 检查点操作

不要把每个内部控制都暴露成普通设置。优先提供简单模式。

### 测试

每个阶段需要：

- 纯服务单元测试
- Harness/Coordinator 行为集成测试
- 新领域模型序列化测试
- 新执行路径的权限/审计测试

标准验证：

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
git diff --check
```

## 7. 近期下一步

下一项具体实现建议从阶段 1 开始：

```text
工具结果摘要和产物引用
```

最小切片：

1. 增加 `AgentArtifact` 领域模型。
2. 在 `AgentRun` 上增加 artifact 列表。
3. 增加 `ToolResultSummarizer`。
4. 先只摘要大型工具输出。
5. 保持原始输出仍可在调用详情和工具追踪中查看。
6. 增加摘要/引用行为测试。

这能立即降低 token 消耗，并为后续 Memory 和 Context Router 打基础，同时不会过度改变核心模型/工具循环。
