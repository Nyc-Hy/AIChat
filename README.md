# AIChat

AIChat is a .NET 8 WPF desktop application for project-scoped LLM conversations and local code-agent workflows.

## Features

- **Project-scoped conversations** — each project has its own conversation history and settings
- **Multi-provider support** — OpenAI-compatible and Anthropic protocol implementations
- **Agent harness** — model/tool loop with planning, execution, verification, and auto-repair
- **14 built-in tools** — file read/write/edit, search, patch, git operations, build, test, shell
- **Tool permission model** — disabled, read-only auto, per-call confirmation, session approval
- **Project-level overrides** — per-project tool permission overrides that merge with global settings
- **Audit logging** — JSONL audit trail for tool calls, approvals, rejections, and run lifecycle
- **Agent run history** — browse, filter, retry, and continue past runs
- **Verification system** — configurable build/test commands with auto-repair loop
- **Context engineering** — file index, budgeted context pack, pinned context items
- **Change control** — snapshot-based conflict detection for safe rollback

## Run

```powershell
dotnet run --project src\AIChat.App\AIChat.App.csproj
```

On first launch, open Settings, choose a provider template, enter the API key, and add it to the configured provider list.

## Test

```powershell
dotnet test AIChat.sln
```

## Agent Mode

When the active model supports tool calls, AIChat enters agent mode automatically. The agent:

1. Receives your goal and project context
2. Creates a plan (visible in the details panel)
3. Executes tools to read, modify, and verify code
4. Runs verification commands (if configured) and auto-fixes failures
5. Records all changes with snapshots for safe rollback

### Tool Permissions

Each tool has a permission mode:

| Mode | Behavior |
|---|---|
| Disabled | Tool is hidden from the model |
| Auto ReadOnly | Read-only tools run without confirmation |
| Confirm Each Time | Every call requires user approval |
| Allow for Session | After first approval, auto-approves for the rest of the run |

Global defaults are in Settings > Tools. Per-project overrides can be added in the same panel.

### Agent Run History

All agent runs are persisted with the conversation. Use the history panel to:

- Browse past runs (filter by status: all, retryable, failed, completed, running)
- View detailed execution steps, file changes, and verification results
- Retry a failed run from scratch
- Continue a stopped or completed run with additional instructions
- Copy a review packet for sharing or debugging

### Verification & Auto-Repair

Configure verification commands per project (e.g., `dotnet build`, `dotnet test`). After the agent makes file changes:

1. Verification commands run automatically
2. If any fail, the failure summary is fed back to the model
3. The model attempts to fix the issues
4. This repeats up to the configured max rounds (default: 3)

## Architecture

```text
src/
  AIChat.App/                  WPF shell, MVVM state, composition root
  AIChat.Domain/               Pure domain models (chat, projects, audit, context)
  AIChat.Abstractions/         Contracts and DTOs used across boundaries
  AIChat.Application/          Agent harness, tools, prompting, context, verification
  AIChat.Providers.OpenAI/     OpenAI-compatible protocol adapter
  AIChat.Providers.Anthropic/  Anthropic protocol adapter
  AIChat.Storage.Json/         Local JSON persistence (%APPDATA%\AIChat)
tests/
  AIChat.Tests/                Unit tests for tools, harness, providers, serialization
```

### Layer Rules

1. UI (`AIChat.App`) owns MVVM state and composition. No business logic.
2. Domain (`AIChat.Domain`) is pure POCOs. No dependencies on other projects.
3. Application (`AIChat.Application`) owns the agent loop, tools, and prompting.
4. Providers (`AIChat.Providers.*`) adapt protocol-specific APIs to the common `IChatProvider` contract.
5. Storage (`AIChat.Storage.Json`) persists domain objects to local JSON files.

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — layer diagram, project structure, and design rules
- [Agent Harness](docs/AGENT_HARNESS.md) — agent loop lifecycle, tool budget, and recovery
- [Tool Security](docs/TOOL_SECURITY.md) — permission model, path guard, and shell policy
- [A2A Adapter Design](docs/A2A_ADAPTER_DESIGN.md) — future Agent-to-Agent boundary design
- [Development Roadmap](docs/REMAINING_DEVELOPMENT_PLAN.md) — current state, maintenance priorities, and future roadmap

## Publish

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained false
```

The output is a framework-dependent deployment. .NET 8 runtime must be installed on the target machine.

For a self-contained build (includes the runtime):

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained true
```
