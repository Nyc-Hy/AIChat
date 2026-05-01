# Architecture

## Layer Diagram

```
┌─────────────────────────────────────────────────┐
│                   AIChat.App                     │
│  WPF Shell · MVVM · Composition Root · XAML      │
├─────────────────────────────────────────────────┤
│              AIChat.Application                  │
│  Agent Harness · Tools · Prompting · Context     │
│  Verification · Routing · Workspace              │
├─────────────────────────────────────────────────┤
│  AIChat.Providers.OpenAI │ AIChat.Providers.Anthropic │
│  Protocol Adapters (IChatProvider)               │
├─────────────────────────────────────────────────┤
│              AIChat.Abstractions                 │
│  Contracts · DTOs · Configuration                │
├─────────────────────────────────────────────────┤
│               AIChat.Domain                      │
│  Pure POCOs · Chat · Projects · Audit · Context  │
├─────────────────────────────────────────────────┤
│             AIChat.Storage.Json                  │
│  Local JSON Persistence (%APPDATA%\AIChat)       │
└─────────────────────────────────────────────────┘
```

## Dependency Rules

- **App** depends on Application, Abstractions, Domain, Providers, Storage.
- **Application** depends on Abstractions, Domain.
- **Providers** depend on Abstractions, Domain.
- **Abstractions** depends on nothing.
- **Domain** depends on nothing.
- **Storage** depends on Domain, Abstractions.

Domain is the innermost layer. No project depends on App.

## Key Abstractions

| Interface | Location | Purpose |
|---|---|---|
| `IChatProvider` | Abstractions | Protocol-specific LLM adapter |
| `IAgentTool` | Application | Tool definition + execute |
| `IAppRepository` | Abstractions | Settings and project persistence |
| `IContextEstimator` | Abstractions | Token count estimation |
| `IExternalToolProvider` | Application | Future MCP/A2A tool source |

## Data Flow

```
User Input
    │
    ▼
MainViewModel.SendAsync()
    │
    ├─ Build context (file index, workspace summary, pinned items)
    ├─ Build system prompt (rules + tools + context pack)
    ├─ Create ChatRequest
    │
    ▼
AgentHarness.RunAsync()
    │
    ├─ AgentRunner.RunAsync() ──► IChatProvider.SendAsync()
    │       │
    │       ▼
    │   Model returns tool_calls
    │       │
    │       ▼
    │   ToolExecutionService.ExecuteAsync()
    │       │
    │       ├─ Check permission mode
    │       ├─ Request approval if needed
    │       ├─ Execute IAgentTool
    │       └─ Return result to model
    │
    ├─ Record steps, file changes, plan updates
    ├─ Run verification (if configured)
    └─ Emit events to UI
```

## Persistence

All data is stored locally under `%APPDATA%\AIChat\`:

- `settings.json` — app settings (providers, tools, permissions)
- `projects.json` — all project workspaces, conversations, agent runs
- `audit/<project-id>.jsonl` — audit event log per project

No data leaves the machine except LLM API calls to configured providers.
