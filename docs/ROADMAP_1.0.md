# AIChat 1.0 Roadmap

AIChat 1.0 should make the CLI/TUI product reliable enough for everyday Vibe Coding work across macOS, Linux, and Windows.

The 1.0 goal is not a larger feature surface. The goal is a smaller, sharper assistant that is accurate, fast, low-cost, cache-friendly, and easy to diagnose.

## Product Pillars

1. CLI/TUI first.
2. Conservative defaults.
3. Explicit tool approval and write behavior.
4. Context that is visible, bounded, and cache-friendly.
5. Release artifacts that are easy to install and verify.
6. Real smoke tests against target model families.

## Milestones

### 0.6.0 Context And Cache Discipline

- Add `aichat context` for project context diagnostics.
- Show project snapshot, file index shape, context token budget, included files, omitted relevant files, verification commands, and cache hints.
- Keep context diagnostics model-free and read-only.
- Make cache-friendly behavior visible before spending model tokens.

### 0.7.0 TUI Daily Use

- Improve TUI status output with context summary and last run outcome. Started with `/context [goal]`.
- Add command history affordances where the terminal supports it.
- Add clearer approval prompts for write, shell, build, test, and git operations.
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

### 1.0.0 Stable CLI/TUI

- Publish checksum-verifiable release artifacts.
- Document the stable command surface.
- Document what remains advanced/experimental.
- Keep WPF as Windows-only and outside the cross-platform release promise.

Status: implemented in the CLI/TUI release branch. Platform-specific smoke testing is still required on macOS Apple Silicon and Linux x64 before marking those platforms fully verified.

## 1.0 Acceptance Bar

- Build passes.
- Test suite passes.
- `aichat doctor` reports useful readiness information.
- `aichat context` explains what will enter the prompt before an agent run.
- `aichat ask` works for one-shot coding tasks.
- `aichat tui` works for continuous coding sessions.
- DeepSeek, MiMo, MiniMAX, and generic OpenAI-compatible providers have explicit model profiles or safe fallbacks.
- macOS, Linux, and Windows release packages are smoke-tested.
- Release artifacts include sha256 checksums.

## Non-Goals Before 1.0

- Cross-platform graphical UI.
- Always-on planner.
- Always-on sub-agents.
- Automatic long-term memory writes.
- Benchmark UI as a default product surface.
- Background autonomous multi-agent execution.
