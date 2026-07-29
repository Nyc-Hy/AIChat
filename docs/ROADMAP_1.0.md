# AIChat 1.0 Roadmap

AIChat 1.0 should make the Avalonia desktop UI reliable enough for everyday Vibe Coding work across macOS, Linux, and Windows, with CLI/TUI kept as the secondary terminal interface.

The 1.0 goal is not a larger feature surface. The goal is a smaller, sharper assistant that is accurate, fast, low-cost, cache-friendly, and easy to diagnose.

Product decisions should follow the baseline in [PRODUCT_BASELINE.md](PRODUCT_BASELINE.md).

## Product Pillars

1. Avalonia UI first, CLI/TUI second.
2. Simple, clear conversation-centered UI.
3. Conservative defaults.
4. Explicit tool approval and write behavior.
5. Context that is visible, bounded, and cache-friendly.
6. Per-session input/output/cache usage visibility.
7. Release artifacts that are easy to install and verify.
8. Real smoke tests against target model families.

## Milestones

### 0.6.0 Context And Cache Discipline

- Add `aichat context` for project context diagnostics.
- Show project snapshot, file index shape, context token budget, included files, omitted relevant files, verification commands, and cache hints.
- Keep context diagnostics model-free and read-only.
- Make cache-friendly behavior visible before spending model tokens.

### 0.7.0 Avalonia Daily Use

- Make the Avalonia main flow cover project selection, provider setup, chat, tool approval, context visibility, and run status.
- Keep TUI status output useful for terminal workflows. Started with `/context [goal]`.
- Add clearer approval prompts for write, shell, build, test, and git operations across both UI surfaces.
- Add transcript export for bug reports and PR review packets.

### 0.8.0 Provider Reliability

- Add provider smoke test command that performs a small non-mutating request. Started with `aichat config test`.
- Improve provider error messages for auth, rate limit, timeout, and context length failures.
- Add model profile validation so custom provider/model pairs get predictable defaults.

### 0.9.0 Release Candidate

- Add installation docs for macOS, Linux, and Windows.
- Run real smoke tests on macOS Apple Silicon and Linux x64.
- Verify GitHub Release automation on a release candidate tag.
- Freeze 1.0 defaults unless a bug forces a change.

### 1.0.0 Stable Avalonia + CLI/TUI

- Publish checksum-verifiable release artifacts.
- Document the stable command surface.
- Document what remains advanced/experimental.
- Keep WPF removed; build the stable GUI path on Avalonia.

Status: Avalonia is the main UI target; CLI/TUI remains the fallback and automation surface. Platform-specific smoke testing is still required on macOS Apple Silicon and Linux x64 before marking those platforms fully verified.

## 1.0 Acceptance Bar

- Build passes.
- Test suite passes.
- Avalonia app starts and covers the primary coding workflow.
- Avalonia shows explicit project, conversation, context size, run status, and session usage summaries.
- `aichat doctor` reports useful readiness information.
- `aichat context` explains what will enter the prompt before an agent run.
- `aichat ask` works for one-shot coding tasks.
- `aichat tui` works for continuous coding sessions.
- DeepSeek, MiMo, MiniMAX, and generic OpenAI-compatible providers have explicit model profiles or safe fallbacks.
- macOS, Linux, and Windows release packages are smoke-tested.
- Release artifacts include sha256 checksums.

## Non-Goals Before 1.0

- A second graphical UI stack.
- Always-on planner.
- Always-on sub-agents.
- Automatic long-term memory writes.
- Benchmark UI as a default product surface.
- Background autonomous multi-agent execution.
