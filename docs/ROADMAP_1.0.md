# AIChat 1.0 Roadmap

> **Status: superseded.** This document is preserved for historical reference only.
> The current development plan is the **12-wave Codex Desktop operational parity** plan in **[`docs/CODEX_DESKTOP_PARITY_PLAN.md`](CODEX_DESKTOP_PARITY_PLAN.md)**, which replaces the 1.0 milestone model with a parity-milestone model.
> Release-blocking criteria (P0 / P1 / P2) live in parity plan §10. The versioned [parity tracking table](PARITY_TRACKING.md) records the live acceptance status of every first-level entry.
> Do **not** plan releases against this document; defer to the parity plan.

AIChat 1.0 should make the Avalonia desktop UI reliable enough for everyday Vibe Coding work across macOS, Linux, and Windows.

The 1.0 goal is not a larger feature surface. The goal is a smaller, sharper assistant that is accurate, fast, low-cost, cache-friendly, and easy to diagnose.

Product decisions should follow the baseline in [PRODUCT_BASELINE.md](PRODUCT_BASELINE.md).

## Product Pillars

1. Avalonia UI only — the desktop application is the single product surface.
2. Simple, clear conversation-centred UI.
3. Conservative defaults.
4. Explicit tool approval and write behaviour.
5. Context that is visible, bounded, and cache-friendly.
6. Per-session input / output / cache usage visibility.
7. Release artifacts that are easy to install and verify.
8. Real smoke tests against target model families.

## Milestones

### 0.6.0 Context and Cache Discipline — done

- Project context diagnostics live in `AIChat.Application`; the desktop status bar surfaces the current budget and a compact explanatory tooltip.
- Project snapshots, file indexes, included/omitted candidates, verification commands, and cache hints feed the agent without adding a permanent file-tree panel.
- Context diagnostics are model-free and read-only.

### 0.7.0 Avalonia Daily Use — done

- Avalonia main flow covers project selection, provider setup, chat, tool approval, context visibility, and run status.
- In-app approval card for write, shell, build/test, and git operations.
- Session metrics and tool-round counters in the top bar.
- Run history reachable from the conversation list.

### 0.8.0 Provider Reliability — done

- Provider readiness check from the desktop **Test connection** button. The underlying `ProviderConnectionTester` does a small non-mutating request.
- Provider error messages distinguish auth, rate limit, timeout, and context length failures.
- Model profile validation so custom provider/model pairs get predictable defaults.

### 0.9.0 Release Candidate — partial

- Install instructions for macOS, Linux, and Windows in [INSTALL.md](INSTALL.md).
- macOS Apple Silicon smoke verified on a real machine (see [RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md)).
- Linux x64 release archive built but **not** smoke-tested on a real machine.
- Windows x64 smoke tested on a separate machine.

### 1.0.0 Beta — in progress (this branch)

- Publish checksum-verifiable release artifacts for the Avalonia desktop app.
- Document the stable desktop surface in [INSTALL.md](INSTALL.md) and the in-app empty states.
- Document what remains advanced / experimental in [PRODUCT_SCOPE.md](PRODUCT_SCOPE.md).
- WPF removed; the GUI path is Avalonia-only.

### 1.0.0 Stable — remaining

- End-to-end Avalonia UI smoke with a real MiniMax provider on macOS, Linux, and Windows.
- Linux x64 release archive smoke-tested on a real machine.
- Verify macOS Keychain and Linux Secret Service on release machines, including the explicit session-only warning when a vault is unavailable.
- A regression run on a real coding task (project context → task → tool approval → verification → summary) to confirm the agent loop holds in the UI on each platform.

## 1.0 Acceptance Bar

- Build passes.
- Test suite passes.
- Avalonia app starts and covers the primary coding workflow.
- Avalonia shows explicit project, conversation, context size, run status, and session usage summaries.
- The provider config card supports an end-to-end "save → test connection → send task" flow with a real provider.
- MiniMax (M3) has an explicit model profile; custom OpenAI-compatible endpoints (self-hosted proxies, internal mirrors) work by setting a custom `BaseUrl` in Settings.
- macOS, Linux, and Windows release packages are smoke-tested.
- Release artifacts include SHA-256 checksums.

## Non-Goals Before 1.0

- A second graphical UI stack.
- Always-on planner.
- Always-on sub-agents.
- Automatic long-term memory writes.
- Benchmark UI as a default product surface.
- Background autonomous multi-agent execution.
- A separate CLI / TUI surface.
