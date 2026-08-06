# AIChat

AIChat is a cross-platform Vibe Coding assistant for local repository work. The goal is a small, fast, low-cost coding workbench that lets MiniMax (and any other OpenAI-compatible endpoint, by setting a custom base URL) deliver an experience close to Claude Code — without the Claude Code price tag.

The 1.0 Beta release ships a single product surface: an **Avalonia desktop application** for macOS, Linux, and Windows. The legacy CLI / TUI surface has been removed; everything (provider setup, project context, agent loop, tool approval, run history) lives in the desktop UI.

This project is open source under [Apache License 2.0](LICENSE).

## Features

- **Cross-platform Avalonia desktop UI** — Mac / Linux / Windows from a single codebase.
- **Project-scoped conversations** — every project has its own session history, settings, and verification commands.
- **Single provider with custom-base-URL support** — ships with MiniMax (M3) using the OpenAI-compatible protocol; any other OpenAI-compatible endpoint (self-hosted proxies included) works by setting a custom `BaseUrl` in Settings.
- **Single-agent loop by default** — planner / sub-agents / auto-fix / memory writes are kept off the main path to keep token usage low and behaviour predictable.
- **15 built-in tools** — read / edit / patch / search / plan / Git / build / test / shell.
- **Tool permission model** — disabled, auto-execute read-only, confirm each call, or allow for the session.
- **Project-level permission overrides** — every project can override the global defaults.
- **Agent run history** — browse, filter, retry, and continue historical runs.
- **Verification & auto-fix (opt-in)** — `dotnet build` / `dotnet test`-style commands; off by default so small tasks don't burn extra tokens.
- **Context engineering** — file index, budgeted context packs, pinned context items.
- **Change control** — snapshot- and hash-based conflict detection and safe rollback.

## Run the Desktop App

```bash
dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj
```

On first run, open **Settings** (`⌘,`) to confirm the MiniMax base URL, paste an API key, and click **Test connection**. To point at a self-hosted OpenAI-compatible endpoint instead, change the `BaseUrl` in the same panel — the protocol stays the same. Then add a project from the left sidebar and send a task from the bottom input box.

For UI trials or demos that must not touch your real projects, settings, or
credential vault, launch with an explicit isolated profile:

```bash
AICHAT_ISOLATED_DATA_ROOT="$(mktemp -d)" \
  dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj
```

The isolated profile stores all JSON, artifacts, and pasted-image staging files
under that directory and keeps API keys in memory for that process only.

See [docs/INSTALL.md](docs/INSTALL.md) for packaged installers and checksum verification on each platform.

## Execution Modes

| Mode | Purpose | Default behaviour |
|---|---|---|
| `fast` | Quick questions, small fixes | 6 tool-round budget, no auto-verify |
| `standard` | Default coding loop | 16 tool-round budget, single-agent |
| `deep` | Hard tasks, refactors, repairs | 40 tool-round budget, planner + verify on |

Mode selection lives in the Settings modal. The main workspace stays conversation-first and shows only the active mode and essential run state.

### Tool Permission Model

| Mode | Behaviour |
|---|---|
| Disabled | Not exposed to the model |
| Auto ReadOnly | Read-only tools run without confirmation |
| Confirm Each Time | Every call needs explicit user approval in the UI |
| Allow for Session | First approval auto-allows for the rest of the run |

Global defaults are configurable per project from the desktop settings panel.

## Model Profile

- **MiniMax (M3)** — short action loop, tight tool parameter convergence, 200K context, no vision by default. Custom MiniMax-style endpoints (self-hosted gateways, internal mirrors) work by setting `BaseUrl` in Settings.

## Architecture

```text
src/
  AIChat.App.Avalonia/         Cross-platform Avalonia desktop UI (only product surface)
  AIChat.Domain/               Pure domain models (chat, project, audit, context)
  AIChat.Abstractions/         Cross-boundary contracts and DTOs
  AIChat.Application/          Agent Harness, tools, prompting, context, verification, registries
  AIChat.Providers.OpenAI/     OpenAI-compatible protocol adapter (the only provider adapter; the catalog ships with MiniMax)
  AIChat.Storage.Json/         Local JSON persistence (macOS: ~/Library/Application Support/AIChat/; Linux: ~/.config/AIChat/; Windows: %APPDATA%\AIChat\)
tests/
  AIChat.Tests/                Tools, Harness, Providers, serialisation, ViewModel unit tests
```

### Layering Rules

1. UI (`AIChat.App.Avalonia`) owns interaction and app composition. No business logic.
2. Domain (`AIChat.Domain`) is pure models, no other project references.
3. Application (`AIChat.Application`) owns the agent loop, tools, prompting.
4. Providers (`AIChat.Providers.*`) adapt each model protocol to `IChatProvider`.
5. Storage (`AIChat.Storage.Json`) persists domain objects to local JSON.

## Testing

```bash
dotnet build AIChat.sln --no-restore -m:1
dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1
```

GitHub Actions runs the same build + test on every Pull Request.

## Agent Mode

When the active model supports tool calls, AIChat enters agent mode. The default product path is a simplified single-agent loop:

1. Receive the user goal and project context.
2. Select the minimum context needed.
3. Read, modify, and verify code with the tool registry.
4. Surface changes, verification results, and a follow-up summary.
5. Persist the run for later review.

Planner / sub-agents / benchmark / memory / plugin / MCP / audit detail remain in the code as advanced capabilities but stay off the main path by default.

## Documentation

- [Product baseline](docs/PRODUCT_BASELINE.md)
- [Product scope](docs/PRODUCT_SCOPE.md)
- [Install instructions](docs/INSTALL.md)
- [Launch plan](docs/LAUNCH_PLAN.md)
- [1.0 roadmap](docs/ROADMAP_1.0.md)
- [Release checklist](docs/RELEASE_CHECKLIST.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Agent Harness](docs/AGENT_HARNESS.md)
- [Tool security](docs/TOOL_SECURITY.md)
- [Plugin system](docs/PLUGIN_SYSTEM.md)
- [GitHub workflow](docs/GITHUB_WORKFLOW.md)
- [A2A adapter design](docs/A2A_ADAPTER_DESIGN.md)
- [Remaining development plan](docs/REMAINING_DEVELOPMENT_PLAN.md)
- [Security policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

## Contributing

Track work via GitHub Issues, review with focused Pull Requests, gate merges on CI. See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Credits

- **Nyc-Hy** — project maintainer
- **CodeX** — AI coding collaborator

## Release

The Avalonia desktop app is published as a self-contained per-platform archive:

```bash
# Apple Silicon macOS
dotnet publish src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Linux x64
dotnet publish src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Windows x64
dotnet publish src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

A `scripts/publish-desktop.ps1` helper script produces all four platform archives and a `SHA256SUMS.txt` file. The GitHub Actions `Release Desktop` workflow does the same on a `v*` tag.
