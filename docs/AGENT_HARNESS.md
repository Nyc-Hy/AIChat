# Agent Harness

The Agent Harness is the core orchestration layer that runs the model/tool loop.

## Lifecycle

```
RunStarted
    │
    ▼
┌─────────────────────────────┐
│  AgentRunner.RunAsync()     │◄──── Feed verification failure
│  ├─ Send messages to model  │      back to model (auto-repair)
│  ├─ Model returns response  │
│  ├─ If tool_calls:          │
│  │   ├─ Execute each tool   │
│  │   ├─ Record step         │
│  │   └─ Loop back           │
│  └─ If no tool_calls:       │
│      └─ Done                │
└─────────────────────────────┘
    │
    ▼
Record file changes (snapshot + hash)
    │
    ▼
Run verification commands (if configured)
    │
    ├─ All pass ──► RunCompleted(Completed)
    │
    └─ Some fail ──► Feed failure summary to model
                     ├─ Loop back to AgentRunner (up to MaxAutoFixRounds)
                     └─ RunCompleted(Failed) after exhausting rounds
```

## Key Types

| Type | Purpose |
|---|---|
| `AgentHarness` | Orchestrates the run, emits events, records state |
| `AgentRunner` | Stateless model/tool loop (no mutable instance fields) |
| `AgentHarnessRunRequest` | Input: conversation, goal, settings, context |
| `AgentRunContext` | Runtime config: project path, tools, permissions |
| `AgentRun` | Domain model: persisted run record |
| `AgentStep` | Single tool call or model response |
| `AgentFileChange` | File mutation with snapshot and hash |
| `AgentVerification` | Verification command result |

## Events

The harness emits `AgentHarnessEvent` via `IAsyncEnumerable`:

| Event | Meaning |
|---|---|
| `RunStarted` | Run begins, `AgentRun` created |
| `StepAdded` | New step recorded |
| `ContentDelta` | Model text token |
| `ToolCall` | Model requests tool execution |
| `ToolApprovalRequired` | Waiting for user approval |
| `ToolApprovalRejected` | User rejected the tool call |
| `ToolResult` | Tool execution completed |
| `RawProviderEvent` | Raw protocol event for debugging |
| `RunCompleted` | Run finished (success/fail/cancelled) |

## Auto-Repair Loop

When `AutoVerifyAgentRuns` is enabled:

1. After the initial agent run completes, the harness checks `VerificationCommands`
2. Each command is executed and its output parsed for errors
3. If any verification fails, the failure summary is injected into the conversation
4. `AgentRunner.RunAsync()` is called again with the updated transcript
5. The model sees the failures and attempts to fix them
6. This repeats up to `MaxAutoFixRounds` (default: 3)

The runner is stateless — it has no mutable instance fields — so it can be called multiple times with different transcripts.

## Statelessness

`AgentRunner` is intentionally stateless. All mutable state lives in:

- `AgentRun` (persisted domain model)
- `AgentHarness` (orchestration state)
- `sessionAllowedTools` (local HashSet per run)

This design allows the harness to call the runner multiple times during the auto-repair loop without state corruption.
