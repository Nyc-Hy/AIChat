# AIChat v1.0.0 Beta — Release Notes

AIChat v1.0.0 Beta is the first **desktop-only** release of the project. The Avalonia application for macOS, Linux, and Windows is the only product surface. The legacy CLI / TUI has been removed.

The release focuses on a practical Claude Code style workflow for third-party coding models: accurate task execution, fast feedback, low token usage, explicit tool approval, and visible context diagnostics.

## Highlights

- Stable cross-platform Avalonia desktop UI for macOS, Linux, and Windows.
- Project-scoped conversations with persistent settings and run history.
- Compact model-free context budget diagnostics in the status bar and tooltip.
- Provider readiness check from the provider config card.
- Fast / Standard / Deep execution modes.
- First-class MiniMax (M3) model profile. Custom OpenAI-compatible endpoints (self-hosted proxies, internal mirrors) work by setting a custom `BaseUrl` in Settings.
- Conservative default agent behaviour: single-agent loop, planner off, sub-agents off, auto-fix off, memory writes off.
- Explicit approval for write, shell, build/test, and git mutation tools — surfaced as an in-app approval card.
- macOS Apple Silicon, Linux x64, and Windows x64 release archives.
- SHA-256 checksum files for every release artifact.

## Desktop UI Surface

The Avalonia window is built around two persistent areas plus focused overlays:

- **Left rail** — current project + project list + recent conversations + project health.
- **Centre** — page title + session metrics + conversation activity stream + prompt input.
- **Overlays** — Settings, Git diff, Memory, keyboard help, and a highest-priority tool approval dialog.

The file tree/preview surface was removed intentionally. Users reference known files with `@file`; the agent discovers repository files through its read/search tools, and resulting changes are reviewed in the Git diff view.

Provider templates supported out of the box: MiniMax (OpenAI-compatible). Custom OpenAI-compatible endpoints (self-hosted MiniMax-style gateways, internal mirrors) work by setting a custom `BaseUrl` in Settings. Earlier 0.5 supported DeepSeek, MiMo, generic OpenAI-compatible, and Anthropic-compatible providers; the 1.0 Beta Provider prune retired all but MiniMax.

## Agent Mode

When the active model supports tool calls, the desktop app enters agent mode with a single-agent loop. The bottom input box sends a task; the centre activity stream renders the conversation with Markdown and surfaces tool calls, approvals, and verification results. Risky actions — file writes, shell commands, tests, builds, git mutations — pause for an in-app approval card.

## Verification

Validated locally on macOS Apple Silicon and Windows x64.

### macOS Apple Silicon (M-series, arm64)

- OS: macOS 26.5.2 (Darwin 25.5.0, arm64)
- Runtime: .NET 10.0.8 (self-contained, single-file Avalonia app)
- Build: `dotnet build AIChat.sln` — 0 warnings, 0 errors.
- Tests: `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj` — current automated suite passes on net10.0.
- Avalonia app: `dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj` — window opens, sidebar / conversation workspace / input box render, theme toggle works, project picker wired.
- Architecture: Apple Silicon `osx-arm64` self-contained publish succeeds (89 MB single-file binary).

### Windows x64 (validated on a separate machine)

- Build passes with 0 warnings and 0 errors.
- Test suite passes.
- Avalonia app: window opens, primary coding workflow (project select → provider config → send task) is reachable from the UI.
- WPF shell is fully removed; the GUI path is Avalonia-only.

### Known gaps

- Linux x64 release archive is built but has not been smoke-tested on a real Linux machine yet.
- API keys use Windows DPAPI, macOS Keychain, or Linux Secret Service. If the platform vault is unavailable, the key is session-only, the UI warns that it must be re-entered after restart, and no plaintext key is written to settings.json.
- The Avalonia app has not been end-to-end smoke-tested with a real MiniMax provider on a real conversation yet. The 1.0.0 GA release will require a real provider round trip from the Avalonia UI.

Before calling a platform fully verified, run the smoke flow from [INSTALL.md](INSTALL.md) on that platform.

## Breaking Changes from 0.5.0

0.5 was a CLI / TUI surface; 1.0 is desktop-only. The following changes are **silent** for users who already have settings.json from 0.5 — the app boots, settings are migrated in place, and a one-time `ProviderSettingsService.Normalize` pass rewrites the provider id and host. If your 0.5 settings still reference a removed provider, see the migration section below.

- **Provider prune: 5 → 1**. `DeepSeek` / `MiMo` / `generic OpenAI-compatible (v0.5)` / `Anthropic-compatible` are no longer in the catalog. `MiniMax` (M3) is the only entry; `BaseUrl` defaults to `https://api.minimax.io/v1`.
- **0.5 BaseUrl rewriting**: if your stored `BaseUrl` host matches the legacy list (`api.anthropic.com` / `api.deepseek.com` / `token-plan-cn.xiaomimimo.com` / `api.xiaomimimo.com`), the app rewrites it to the MiniMax default on the next launch. Self-hosted proxies at non-legacy hosts are preserved.
- **CLI / TUI removed**. There is no `aichat` binary in 1.0. Use the desktop app, or call the `AIChat.Application` libraries from your own .NET host.
- **WPF shell removed**. The Windows-only WPF startup path is gone; the GUI path is Avalonia-only on all three platforms.
- **API key storage**: Windows moved to DPAPI current-user; macOS uses Keychain (with a "Allow all applications to access this item" prompt the first time per keychain entry); Linux uses Secret Service. The 0.5 "plain text" fallback was retired in 0.5.1; 1.0 is session-only when the platform vault is unavailable.
- **Env var override (new)**: `AICHAT_API_KEY` (or `AICHAT_PROVIDER_<NAME>_API_KEY`) bypasses the platform vault entirely. See [`docs/PRIVACY.md`](PRIVACY.md).
- **Crash log (new)**: every unhandled exception is appended to `<dataDir>/crash.log`. The next launch shows a one-time toast if a new entry was added.
- **Removed subsystems** (per AGENTS.md pitfall class 6 + user 主动删除): `FileTreeView` / `FilePreviewView` / `FileTreeBuilder` and their view-models. Reference files via `@file` or the agent's read / search tools.

## Migration from 0.5.0

1. **Settings file**: 0.5 → 1.0 reads `settings.json` in place. `JsonAppRepository.LoadSettingsAsync` upgrades it; no manual conversion needed.
2. **API key**: same service-name scope on each platform, so the keychain / DPAPI / Secret Service entry persists. The 1.0 launch will re-decrypt the stored secret; you do not need to re-enter it.
3. **Provider migration**: if your 0.5 `ProviderId` was `deepseek` / `tokenplan-mimo` / `anthropic` / `openai-compatible`, 1.0 silently switches to `minimax` and rewrites the host (if it was a known legacy host). Your stored API key carries over.
4. **CLI usage**: 0.5 scripts that ran `aichat ask "..."` need a different shape in 1.0. Either drive the desktop app interactively, or call `AIChat.Application` from your own .NET host (the libraries are still there).
5. **Data path**: 0.5 used `~/.config/AIChat/` on Linux; 1.0 uses the same. macOS 0.5 used `~/Library/Application Support/AIChat/`; 1.0 uses the same. Windows 0.5 used `%APPDATA%\AIChat\`; 1.0 uses the same. No data moves.
6. **Env var for CI / headless**: 0.5's `AICHAT_CONFIG_PATH` is no longer relevant. 1.0's `AICHAT_API_KEY` / `AICHAT_PROVIDER_<NAME>_API_KEY` / `AICHAT_ISOLATED_DATA_ROOT` are the three you care about.

## Known Limitations in 1.0 Beta

These are the P1 items from [`docs/SHIP_REPORT_2026-08-02.md` §4](SHIP_REPORT_2026-08-02.md) that are not in 1.0 Beta. The app boots and the primary coding workflow works without any of them; the limitations are about completeness, not correctness.

- **Sub-agent stop / cancel / redirect** (Wave 7 follow-up) — sub-agents are visible in the Environment panel but cannot be stopped or redirected mid-run.
- **Real cron scheduling** (Wave 9 follow-up) — Scheduled tasks fire a "Run now" record but do not actually execute the prompt on a real schedule. The infrastructure (registry + UI) is in place; the engine is the gap.
- **Plugin install / uninstall / capability grants** (Wave 8 follow-up) — the registry loads manifests from `~/.aichat/plugins/*/plugin.json`, but there is no in-app install flow yet.
- **Real local preview + cloud deploy adapter** (Wave 9 follow-up) — Sites preview runs `python3 -m http.server` and is wired to the BackgroundProcessSupervisor; cloud deploy is hidden behind a `IsEnabled=False` button (per plan §5.4).
- **Settings full-page route** (Wave 10 follow-up) — current Settings is a modal; Codex uses a full page.
- **Settings 12 H2 sections complete** (Wave 10 follow-up) — 4 of 12 sections are first-slice; smart-snapshot, hooks, worktree, connections, environment, etc. are deferred.
- **Cross-platform real-machine smoke** (Wave 11 follow-up) — Linux x64 build succeeded but a real-machine test pass is pending for 1.0 GA.
- **Real provider smoke** (Wave 11 follow-up) — automated tests use mocked providers; a real MiniMax round trip from the Avalonia UI is pending for 1.0 GA.
- **No macOS code signing / notarization** — the 1.0 Beta install is unsigned. macOS users will see a "from an unidentified developer" prompt the first time they open the `.app`; the [`INSTALL.md`](INSTALL.md) "macOS" section explains the Gatekeeper workaround. A signed / notarized build is the 1.0 GA gate.
