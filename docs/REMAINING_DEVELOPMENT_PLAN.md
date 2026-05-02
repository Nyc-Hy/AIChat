# AIChat Development Plan

This document summarizes what has been built, what should be optimized next, and what major features remain for the future.

For a step-by-step task breakdown aimed at low-context models, see [NEXT_DEVELOPMENT_HANDOFF_PLAN.md](NEXT_DEVELOPMENT_HANDOFF_PLAN.md).

## Completed Capabilities

Phases 1 through 16 are complete. The project has a working foundation:

### Core Platform (Phases 1-6)

- WPF desktop shell with MVVM, project-scoped conversations and settings.
- OpenAI-compatible and Anthropic provider adapters.
- Agent Harness with model/tool loop, planning, execution, verification, and auto-repair.
- 14 built-in tools: file read/write/edit, search, patch, git, build, test, shell.
- `ProjectPathGuard` restricting tools to the current project.
- Tool budget, approval guardrails, and end-of-run validation.
- Agent run history with filtering (all, retryable, failed/stopped, completed, running).
- Recovery suggestions and copyable review packets.
- Snapshot-based conflict detection for safe file rollback.

### Advanced Features (Phases 7-14)

- Structured agent plans via `update_plan` internal tool.
- Resumable runs with `ContinuedFromRunId` and continue/retry actions.
- Single-run agent queue preventing concurrent executions.
- Project file index (`ProjectFileIndexBuilder`) with directory scanning and file classification.
- Budgeted context pack (`ProjectContextPackBuilder`) with pinned context items.
- Unified file change attribution (snapshot, hash, step linkage).
- Guarded `apply_patch` tool with find/replace and unique-match enforcement.
- Verification commands with `VerificationResultParser` and bounded auto-fix loops.
- Model capability gating (`SupportsTools`) with UI fallback to plain chat.
- Tool permission model: disabled, auto read-only, confirm each time, allow for session.
- Project-level tool permission overrides merged with global defaults.
- JSONL audit logging for tool calls, approvals, rejections, and run lifecycle.
- `AgentToolRegistry` with metadata (category, default permission, grouping).
- `IExternalToolProvider` interface for future MCP/A2A tool registration.
- A2A adapter design document (`docs/A2A_ADAPTER_DESIGN.md`).

### Polish & Docs (Phases 15-16)

- Agent status bar showing current phase, tool, budget, and plan progress.
- Agent run details organized into tabs (overview, plan, file changes, verification).
- Warning badges for missing API key and unsupported tool models.
- README with agent mode, tool permissions, run history, architecture, and publish instructions.
- Architecture docs: `docs/ARCHITECTURE.md`, `docs/AGENT_HARNESS.md`, `docs/TOOL_SECURITY.md`.
- Version display in window title and publish configuration.

## Optimization Priorities

These are the next recommended tasks. They reduce maintenance cost, improve observability, and strengthen test coverage before adding new features.

See [NEXT_DEVELOPMENT_HANDOFF_PLAN.md](NEXT_DEVELOPMENT_HANDOFF_PLAN.md) for detailed task breakdowns.

| Priority | Task | Goal |
|----------|------|------|
| High | Clean up old plan docs | Remove stale phase details, lower handoff cost |
| High | Extract MainViewModel history filter | Reduce 3400-line UI file complexity |
| High | Extract review packet builder | Move string generation out of UI class |
| Medium-High | Add audit tab in agent run details | Improve observability of tool actions |
| Medium-High | Cover Anthropic tool call parsing | Reduce provider protocol risk |
| Medium | Add file type statistics to context pack | Help model understand project structure |
| Medium | Cover approval/audit consistency | Lock down rejection and session-approval behavior |
| Medium-Low | Polish version display and publish docs | Ensure users can identify and deploy builds |

## Future Features

These are larger features that should be built after the optimization pass. Each one extends the existing Harness, permission, and audit systems rather than replacing them.

### MCP Client Integration

Implement `McpToolProvider` behind the existing `IExternalToolProvider` interface. Allow AIChat to discover and use tools from external MCP servers. All external tool calls must go through the Harness approval and audit pipeline.

### A2A Server

Expose AIChat as an Agent that external systems can invoke via the Agent-to-Agent protocol. Inbound requests must pass through the same Harness, permission, and workspace protection as local user requests. See `docs/A2A_ADAPTER_DESIGN.md` for the boundary design.

### Multi-Agent Queue

Extend the single-run queue to support concurrent or queued agent runs. Requires careful isolation of workspace state, tool approvals, and audit trails between runs.

### Context Engineering Enhancements

- File extension statistics in context packs (e.g., ".cs: 42 files").
- Smarter file relevance scoring based on recent edits and conversation topics.
- Incremental index updates instead of full rescan.

### Desktop Experience

- Installer and auto-updater.
- Theme customization.
- Keyboard shortcuts for common agent actions.

## Development Principles

1. Maintain layering: UI in `AIChat.App`, agent orchestration in `AIChat.Application`, domain models in `AIChat.Domain`, protocol adapters in `AIChat.Providers.*`, persistence in `AIChat.Storage.Json`.
2. Do not expand `MainViewModel.cs`. Put new core logic in Application or independent services.
3. One small goal per commit. Do not mix UI, Provider, Harness, and Storage changes.
4. Tool and agent changes must consider permissions, audit, recovery, and tests.
5. File-write, shell, and git-modification features must remain conservative. Never bypass `ProjectPathGuard` or approval mechanisms.
6. Do not delete or roll back uncommitted user changes. Check `git status --short` before starting.

## Delivery Template

After completing any task, report using this format:

```text
Completed:
- ...

Files changed:
- ...

Verification:
- dotnet build AIChat.sln --no-restore
- dotnet test AIChat.sln --no-restore

Risks / Notes:
- ...

Next suggested task:
- ...
```
