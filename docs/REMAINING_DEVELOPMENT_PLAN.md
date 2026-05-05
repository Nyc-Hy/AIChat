# AIChat Development Roadmap

This document is the current planning entry point for AIChat. It replaces the older phase-by-phase handoff notes, which were useful during early development but had become stale after the optimization pass was completed.

## Current State

AIChat is a .NET 8 WPF desktop application for project-scoped LLM conversations and local code-agent workflows.

The stable foundation now includes:

- WPF desktop shell with MVVM, project-scoped conversations, settings, and persisted run history.
- OpenAI-compatible and Anthropic provider adapters, including tool-call request/response handling.
- Agent Harness with model/tool loop, planning, execution, verification, auto-repair, retry, and continue flows.
- Built-in tools for file read/write/edit, search, patch, git operations, build/test, and shell execution.
- Tool permissions, project-level overrides, approval flow, shell safety checks, and project path protection.
- JSON persistence, JSONL audit logging, audit display in agent run details, and copyable review packets.
- Project file indexing, budgeted context packs, pinned context items, and file type summaries.
- Snapshot/hash-based file change tracking for conflict-aware rollback.
- Version display and publish instructions.

## Maintenance Priorities

Use these priorities before starting larger features:

| Priority | Area | Goal |
|---|---|---|
| High | `MainViewModel` size | Continue extracting pure workspace, audit, and agent-run logic into small services. |
| High | Test coverage | Keep provider protocol parsing, tool approval, audit consistency, and workspace safety covered. |
| Medium | Context quality | Improve relevance scoring, recent-file selection, and incremental indexing without increasing prompt noise. |
| Medium | Observability | Make agent failures easier to inspect through clearer run summaries, audit grouping, and verification output. |
| Medium-Low | Packaging | Improve release packaging after the framework-dependent publish path remains reliable. |

## Future Features

These are larger efforts. They should extend the existing Harness, permission, audit, and verification systems instead of bypassing them.

### MCP Client Integration

Implement `McpToolProvider` behind `IExternalToolProvider` so AIChat can discover and use tools from external MCP servers. External tool calls must still go through the normal approval and audit pipeline.

### A2A Server

Expose AIChat as an Agent that external systems can invoke. Inbound requests must use the same Harness, permission model, path guard, audit trail, and verification loop as interactive runs. See [A2A Adapter Design](A2A_ADAPTER_DESIGN.md).

### Multi-Agent Queue

Extend the single-run queue to support queued or concurrent agent runs. This requires workspace isolation, separate tool approvals, and clear audit attribution per run.

### Context Engineering Enhancements

- Smarter file relevance scoring based on recent edits and conversation topics.
- Incremental index updates instead of full rescans.
- Better prompt shaping for large repositories.

### Desktop Experience

- Installer and auto-updater.
- Theme customization.
- Keyboard shortcuts for common agent actions.

## Development Principles

1. Maintain layering: UI in `AIChat.App`, agent orchestration in `AIChat.Application`, domain models in `AIChat.Domain`, protocol adapters in `AIChat.Providers.*`, persistence in `AIChat.Storage.Json`.
2. Avoid expanding `MainViewModel.cs`; move reusable logic to independent services.
3. Keep changes small and focused. Do not mix UI, Provider, Harness, and Storage work unless the feature requires it.
4. Tool and agent changes must account for permissions, audit, recovery, and tests.
5. File-write, shell, and git-modification features must remain conservative. Never bypass `ProjectPathGuard` or approval mechanisms.
6. Do not delete or roll back uncommitted user changes. Check `git status --short` before starting.

## Verification

For code changes:

```powershell
dotnet build AIChat.sln --no-restore
dotnet test AIChat.sln --no-restore
git diff --check
```

For documentation-only changes:

```powershell
git diff --check
```
