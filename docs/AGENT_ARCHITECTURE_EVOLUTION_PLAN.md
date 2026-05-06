# Agent Architecture Evolution Plan

本文档描述 AIChat 从当前“单 Agent 线性工具循环”演进到“阶段化、可调度、上下文经济型 Agent 系统”的开发计划。

目标不是一次性重写 Agent，而是在现有 Harness、权限、审计、验证、持久化基础上逐步扩展。每一阶段都应保持可运行、可测试、可回滚。

## 1. Current Baseline

当前 AIChat 已具备稳定的本地代码 Agent 基座：

- `MainViewModel` 发起用户请求、更新 UI、处理审批、保存运行结果。
- `AgentRequestFactory` 构建 `ChatRequest`、`AgentRunContext` 和 request snapshot。
- `AgentHarness` 记录 `AgentRun`、steps、file changes、verification、plan。
- `AgentRunner` 执行模型/tool loop。
- `ToolExecutionService` 负责工具权限、审批和实际执行。
- `AgentRunAuditService` 记录审计事件。
- `JsonAppRepository` 持久化 settings/projects/conversations/runs。
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

## 2. Target Architecture

目标架构：

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

1. LLM decides semantic next steps.
2. System enforces permissions, budgets, risk rules, and scheduling.
3. Context is routed before prompt composition; do not inject everything by default.
4. Agent instances are created on demand from predefined templates.
5. Tool results are summarized and referenced instead of blindly appended.
6. Long tasks run in phases with checkpoints, not one unbounded loop.

## 3. Missing Capabilities

| Capability | Current State | Needed |
|---|---|---|
| Planner | Model may call `update_plan`, but no system planning phase | Structured planner output before execution |
| Coordinator | `MainViewModel` and `AgentHarness` run a single loop | System-level state machine and scheduling policy |
| Prompt Composer | Mostly static system prompt and accumulated transcript | Phase/model/task-specific prompt generation |
| Context Router | Budgeted conversation context and file index | Task-aware retrieval, refs, recent-change scoring |
| Tool Result Summarizer | Tool output enters transcript directly | Summary + artifact ref + on-demand expansion |
| Memory Layer | Conversation history and pinned context | User, project, task, tool-result memory |
| Agent Templates | One `AgentRunner` role | Planner, Explorer, Worker, Verifier, Summarizer, Reviewer templates |
| Sub-agent Runtime | None | On-demand scoped child agents with budgets and write scopes |
| Budget Manager | Tool-call limit and auto-fix limit | Token, time, tool, phase, and agent budgets |
| Artifact Store | Call detail/audit/project persistence | Structured artifact refs for raw outputs, logs, diffs, summaries |
| Multimodal Intake | Mostly text/code | Images, documents, screenshots, and extracted summaries |

## 4. Development Phases

### Phase 1: Tool Result Summaries and Artifact References

Goal: reduce token waste before adding more orchestration.

Deliverables:

- Add an `AgentArtifact` domain model:
  - `Id`
  - `RunId`
  - `StepId`
  - `Kind`
  - `Summary`
  - `Content`
  - `CreatedAt`
  - optional metadata dictionary
- Add `AgentArtifactStore` or repository methods for storing artifacts with the project/run.
- Add `ToolResultSummarizer` in Application:
  - summarize search output
  - summarize file reads
  - summarize command output
  - summarize diffs
  - preserve raw output as artifact
- Change `AgentRunner` or `ToolExecutionService` boundary so large tool results can become:

```text
summary for LLM + artifact reference for UI/detail inspection
```

Do not change tool permission behavior.

Acceptance criteria:

- Large tool output does not fully enter the next LLM request by default.
- UI can still display or copy raw tool output.
- Audit still records tool execution.
- Existing tool tests still pass.
- New tests cover truncation/summarization/ref behavior.

Suggested files:

- `src/AIChat.Domain/Chat/AgentArtifact.cs`
- `src/AIChat.Application/Agents/ToolResultSummarizer.cs`
- `src/AIChat.Application/Agents/AgentArtifactService.cs`
- `tests/AIChat.Tests/Agents/ToolResultSummarizerTests.cs`

### Phase 2: Structured Planner

Goal: add an explicit planning phase before execution.

Deliverables:

- Add domain/application DTOs:
  - `AgentStructuredPlan`
  - `AgentPlanPhase`
  - `AgentPlanTask`
  - `AgentPlanRisk`
  - `AgentPlanBudget`
- Add `PlannerPromptBuilder`.
- Add `AgentPlanner` that calls the configured LLM and asks for structured JSON.
- Add JSON parser and validation:
  - reject empty plan
  - cap task count
  - normalize unknown phases
  - extract suggested tools/context
- Persist structured plan into `AgentRun.Plan` or a new plan property.

Initial planner should not spawn sub-agents. It only produces a plan for the current run.

Acceptance criteria:

- Agent run starts with a validated structured plan.
- Bad JSON planner output falls back to a simple single-phase plan.
- UI can display phases/tasks.
- Tests cover valid plan, invalid plan fallback, and risk/budget normalization.

Suggested files:

- `src/AIChat.Application/Agents/Planning/AgentPlanner.cs`
- `src/AIChat.Application/Agents/Planning/PlannerPromptBuilder.cs`
- `src/AIChat.Application/Agents/Planning/AgentStructuredPlanParser.cs`
- `tests/AIChat.Tests/Agents/Planning/AgentPlannerTests.cs`

### Phase 3: Coordinator State Machine

Goal: move run orchestration from a single implicit loop into explicit phases.

Target states:

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

Deliverables:

- Add `AgentCoordinator`.
- Add `AgentRunPhase` enum.
- Add phase transition events.
- Add per-phase status and summary fields to `AgentRun`.
- Update `AgentHarness` so it can delegate high-level decisions to coordinator.
- Keep `AgentRunner` as the low-level model/tool loop.

Acceptance criteria:

- Existing single-agent behavior remains functionally equivalent.
- Run details can show current phase.
- Verification and repair are represented as phases.
- Tests cover phase transitions and cancellation.

Suggested files:

- `src/AIChat.Application/Agents/Coordinator/AgentCoordinator.cs`
- `src/AIChat.Application/Agents/Coordinator/AgentRunPhase.cs`
- `tests/AIChat.Tests/Agents/Coordinator/AgentCoordinatorTests.cs`

### Phase 4: Prompt Composer

Goal: replace one-size-fits-most prompts with phase-aware prompts.

Deliverables:

- Add `AgentPromptComposer`.
- Inputs:
  - phase
  - task goal
  - model/provider info
  - plan
  - context refs
  - memory snippets
  - allowed tools
  - budget
- Output:
  - system message
  - developer-style instructions where applicable
  - user task message
  - structured response requirements

Prompt profiles:

- planning
- context gathering
- execution
- verification repair
- summarization
- review

Acceptance criteria:

- Planner, execution, and repair prompts are generated through the composer.
- Prompt size is measurable.
- Tests snapshot key prompt sections without over-constraining wording.

Suggested files:

- `src/AIChat.Application/Prompting/AgentPromptComposer.cs`
- `src/AIChat.Application/Prompting/AgentPromptProfile.cs`
- `tests/AIChat.Tests/Prompting/AgentPromptComposerTests.cs`

### Phase 5: Context Router

Goal: route minimal context by task and phase.

Deliverables:

- Add `ContextRouter`.
- Add task-aware relevance scoring:
  - file path/name match
  - recent edits
  - pinned context
  - conversation mentions
  - test/source pairing
  - project file index type tags
- Add context pack result with refs:

```text
summary
included files
included snippets
artifact refs
omitted-but-relevant refs
estimated tokens
```

- Add incremental index update path if feasible.

Acceptance criteria:

- Context router can return a small pack for a concrete task.
- It does not include large files blindly.
- Token estimate is available before LLM call.
- Tests cover scoring and budget trimming.

Suggested files:

- `src/AIChat.Application/Context/ContextRouter.cs`
- `src/AIChat.Application/Context/TaskContextPack.cs`
- `src/AIChat.Application/Context/FileRelevanceScorer.cs`
- `tests/AIChat.Tests/Context/ContextRouterTests.cs`

### Phase 6: Memory Layer

Goal: separate durable memory from transient transcript.

Memory categories:

- User memory: preferences, style, recurring instructions.
- Project memory: architecture, conventions, important decisions.
- Task memory: current run findings, assumptions, checkpoints.
- Tool memory: summaries and refs for tool outputs.

Deliverables:

- Add memory models and repository support.
- Add memory retrieval by category and relevance.
- Add memory write policies:
  - never silently store secrets
  - store user memory only when explicitly confirmed or safe policy allows
  - project memory should be inspectable/editable
- Add UI surface later; first build API and tests.

Acceptance criteria:

- Planner/context router can request project/task memory.
- Memory entries have source and timestamp.
- Tests cover retrieval, filtering, and no-secret policy.

Suggested files:

- `src/AIChat.Domain/Memory/MemoryEntry.cs`
- `src/AIChat.Application/Memory/MemoryService.cs`
- `src/AIChat.Application/Memory/MemoryRetriever.cs`
- `tests/AIChat.Tests/Memory/MemoryServiceTests.cs`

### Phase 7: Agent Templates

Goal: define role templates before introducing sub-agent spawning.

Templates:

- Planner: creates structured plan.
- Explorer: read-only codebase analysis.
- Worker: edits within assigned scope.
- Verifier: runs checks and explains failures.
- Summarizer: compresses results/artifacts.
- Reviewer: finds risks and missing tests.

Deliverables:

- Add `AgentTemplate`.
- Add `AgentTemplateCatalog`.
- Add default tool permissions per template.
- Add prompt profile per template.
- Add allowed output schema per template.

Acceptance criteria:

- Templates are data/config-driven enough to evolve.
- Coordinator can select a template without spawning yet.
- Tests cover template capabilities and permission defaults.

Suggested files:

- `src/AIChat.Application/Agents/Templates/AgentTemplate.cs`
- `src/AIChat.Application/Agents/Templates/AgentTemplateCatalog.cs`
- `tests/AIChat.Tests/Agents/Templates/AgentTemplateCatalogTests.cs`

### Phase 8: Sub-agent Runtime

Goal: allow Coordinator-approved sub-agents.

Deliverables:

- Add `SubAgentRun`.
- Add `SubAgentScheduler`.
- Add isolated sub-agent context:
  - task
  - minimal context pack
  - tool permissions
  - write scope
  - budget
- Add result contract:

```text
status
summary
findings
changed files
artifact refs
recommended next step
```

- Add safety rules:
  - no duplicate agents for same unresolved task
  - worker write scopes must be disjoint unless explicitly serialized
  - verifier cannot edit
  - explorer cannot edit
  - all tool calls still go through normal permission/audit pipeline

Acceptance criteria:

- Coordinator can run one read-only Explorer sub-agent.
- Parent run receives structured result.
- Audit attributes tool calls to parent and sub-agent.
- Tests cover budget, scope, cancellation, and result aggregation.

Suggested files:

- `src/AIChat.Application/Agents/SubAgents/SubAgentScheduler.cs`
- `src/AIChat.Application/Agents/SubAgents/SubAgentRun.cs`
- `src/AIChat.Application/Agents/SubAgents/SubAgentResult.cs`
- `tests/AIChat.Tests/Agents/SubAgents/SubAgentSchedulerTests.cs`

### Phase 9: Budget Manager and Checkpoints

Goal: support long-running tasks without losing control.

Budgets:

- tool calls
- model tokens
- elapsed time
- per-phase calls
- per-sub-agent calls
- auto-repair rounds
- file mutation count

Deliverables:

- Add `AgentBudget`.
- Add `AgentBudgetManager`.
- Add checkpoint policy:
  - every N tool calls
  - before high-risk mutation
  - before continuing after budget segment
  - after verification failure loops
- Add UI prompt for continue/pause.

Acceptance criteria:

- Long tasks can pause at budget checkpoints.
- User can continue with an additional budget segment.
- Existing hard max tool rounds remains as a final safety cap.
- Tests cover budget consumption and checkpoint triggers.

Suggested files:

- `src/AIChat.Application/Agents/Budget/AgentBudget.cs`
- `src/AIChat.Application/Agents/Budget/AgentBudgetManager.cs`
- `tests/AIChat.Tests/Agents/Budget/AgentBudgetManagerTests.cs`

### Phase 10: Multimodal Intake

Goal: allow images/documents/screenshots to enter the planning/context system as structured artifacts.

Deliverables:

- Add `InputArtifact` model.
- Add artifact extraction pipeline:
  - image description
  - OCR text
  - document summary
  - spreadsheet summary
  - screenshot UI element summary
- Add references from plan/context to input artifacts.
- Add prompt composer support for multimodal summaries.

Acceptance criteria:

- User can attach an image/document and the planner sees a concise summary.
- Raw artifact is inspectable.
- Planner can request more detail from an artifact ref.
- Tests cover text-only fallback and artifact metadata.

Suggested files:

- `src/AIChat.Domain/Artifacts/InputArtifact.cs`
- `src/AIChat.Application/Artifacts/InputArtifactService.cs`
- `tests/AIChat.Tests/Artifacts/InputArtifactServiceTests.cs`

## 5. Recommended Implementation Order

Short version:

1. Tool result summaries and artifact refs.
2. Structured planner.
3. Coordinator state machine.
4. Prompt composer.
5. Context router.
6. Memory layer.
7. Agent templates.
8. Sub-agent runtime.
9. Budget manager and checkpoints.
10. Multimodal intake.

Reasoning:

- Summaries/artifacts solve the biggest token waste first.
- Planner and Coordinator should exist before sub-agents.
- Prompt composer and context router make every later LLM call cheaper and clearer.
- Memory and templates prepare the ground for sub-agents.
- Sub-agents come late because they multiply complexity, audit, permissions, and UI states.

## 6. Cross-Cutting Requirements

### Permissions

All tools, including future sub-agent tools, must continue through:

- `ToolExecutionService`
- `ToolPermissionMode`
- project-level overrides
- approval UI
- audit logging
- `ProjectPathGuard`

No new path may bypass these layers.

### Audit

Every phase and sub-agent should produce auditable events:

- phase started/completed
- planner output accepted/rejected
- sub-agent created/completed
- budget checkpoint reached
- tool result summarized
- artifact stored

### Persistence

Long-running runs need durable state:

- current phase
- structured plan
- artifacts
- task memory
- budget consumption
- sub-agent results
- checkpoint decisions

### UI

UI should show:

- current phase
- plan
- active sub-agent tasks
- budget usage
- artifacts/summaries
- verification status
- checkpoint actions

Avoid exposing every internal control as a normal setting. Prefer simple modes first.

### Testing

Each phase requires:

- unit tests for pure services
- integration tests for harness/coordinator behavior
- serialization tests for new domain models
- permission/audit tests for any new execution path

Standard verification:

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
git diff --check
```

## 7. Near-Term Next Step

The next concrete implementation should be Phase 1:

```text
Tool Result Summaries and Artifact References
```

Minimal first slice:

1. Add `AgentArtifact` domain model.
2. Add artifact list to `AgentRun`.
3. Add `ToolResultSummarizer`.
4. Summarize only large tool outputs first.
5. Keep raw output visible in call details/tool traces.
6. Add tests for summary/ref behavior.

This gives immediate token savings and sets up later memory/context routing work without changing the core model/tool loop too aggressively.
