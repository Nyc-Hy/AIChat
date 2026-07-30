# AIChat v1.0.0 Beta — Release Notes

AIChat v1.0.0 Beta is the first **desktop-only** release of the project. The Avalonia application for macOS, Linux, and Windows is the only product surface. The legacy CLI / TUI has been removed.

The release focuses on a practical Claude Code style workflow for third-party coding models: accurate task execution, fast feedback, low token usage, explicit tool approval, and visible context diagnostics.

## Highlights

- Stable cross-platform Avalonia desktop UI for macOS, Linux, and Windows.
- Project-scoped conversations with persistent settings and run history.
- Model-free context diagnostics in the right-rail context panel.
- Provider readiness check from the provider config card.
- Fast / Standard / Deep execution modes.
- First-class DeepSeek, MiMo, and MiniMAX model profiles.
- Conservative default agent behaviour: single-agent loop, planner off, sub-agents off, auto-fix off, memory writes off.
- Explicit approval for write, shell, build/test, and git mutation tools — surfaced as an in-app approval card.
- macOS Apple Silicon, Linux x64, and Windows x64 release archives.
- SHA-256 checksum files for every release artifact.

## Desktop UI Surface

The Avalonia window is built around three areas:

- **Left rail** — current project + project list + recent conversations + project health.
- **Centre** — page title + session metrics + conversation activity stream + prompt input.
- **Right rail** — context preview, pending tool approval card, safety toggles (read-only / auto-verify), advanced provider configuration.

Provider templates supported out of the box: DeepSeek, MiMo, MiniMAX, generic OpenAI-compatible, Anthropic-compatible.

## Agent Mode

When the active model supports tool calls, the desktop app enters agent mode with a single-agent loop. The bottom input box sends a task; the centre activity stream renders the conversation with Markdown and surfaces tool calls, approvals, and verification results. Risky actions — file writes, shell commands, tests, builds, git mutations — pause for an in-app approval card.

## Verification

Validated locally on macOS Apple Silicon and Windows x64.

### macOS Apple Silicon (M-series, arm64)

- OS: macOS 26.5.2 (Darwin 25.5.0, arm64)
- Runtime: .NET 10.0.8 (self-contained, single-file Avalonia app)
- Build: `dotnet build AIChat.sln` — 0 warnings, 0 errors.
- Tests: `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj` — 621/621 passing on net10.0.
- Avalonia app: `dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj` — window opens, sidebar / context panel / input box render, theme toggle works, project picker wired.
- Architecture: Apple Silicon `osx-arm64` self-contained publish succeeds (89 MB single-file binary).

### Windows x64 (validated on a separate machine)

- Build passes with 0 warnings and 0 errors.
- Test suite passes.
- Avalonia app: window opens, primary coding workflow (project select → provider config → send task) is reachable from the UI.
- WPF shell is fully removed; the GUI path is Avalonia-only.

### Known gaps

- Linux x64 release archive is built but has not been smoke-tested on a real Linux machine yet.
- API keys for configured providers are stored on disk using Windows DPAPI on Windows. On macOS and Linux the current implementation falls back to a "plain" marker — the file is owned by the user account, but is not encrypted at rest. Encrypted protection for non-Windows platforms is tracked as a post-1.0 follow-up.
- The Avalonia app has not been end-to-end smoke-tested with a real provider (DeepSeek / MiMo / MiniMAX) on a real conversation yet — manual `aichat config test` validation still uses the removed CLI flow on this branch. The 1.0.0 GA release will require a real provider round trip from the Avalonia UI.

Before calling a platform fully verified, run the smoke flow from [INSTALL.md](INSTALL.md) on that platform.
