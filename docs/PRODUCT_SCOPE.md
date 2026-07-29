# AIChat Product Scope

AIChat is a Vibe Coding assistant optimized for third-party coding models such as DeepSeek, MiMo, MiniMAX, OpenAI-compatible providers, and Anthropic-compatible providers.

The product goal is not to expose every agent framework capability. The goal is to provide a small, reliable, low-cost coding assistant that can replace a Claude Code style workflow for day-to-day repository work.

The detailed product baseline is maintained in [PRODUCT_BASELINE.md](PRODUCT_BASELINE.md).

## Primary Interface

The primary cross-platform interface is the Avalonia desktop UI. CLI/TUI remains the terminal-first interface for automation, scripting, and keyboard-driven workflows:

- `aichat ask` for one-shot coding tasks.
- `aichat tui` for interactive coding sessions.
- `aichat config` for provider setup.
- `aichat context` for model-free context diagnostics before spending tokens.
- `aichat doctor` for local readiness checks.
- `aichat config test` for provider readiness checks before coding tasks.

The legacy WPF shell has been removed. Avalonia is the main UI direction, while CLI/TUI remains a supported secondary interface.

## Core Priorities

1. Clear and simple Avalonia UI.
2. Explicit project and conversation boundaries.
3. Accurate task execution.
4. Fast response time.
5. Low token usage.
6. Accurate context-size visibility.
7. Per-session input/output/cache usage summaries.
8. Better prompt and provider-cache hit rates.
9. Predictable tool approval and write behavior.
10. Simple install and release process.

## Default Product Shape

Defaults should stay conservative:

- Standard execution mode by default.
- Planner disabled by default.
- Sub-agents disabled by default.
- Auto-fix disabled by default.
- Memory writes disabled unless explicitly requested.
- Tool rounds capped tightly for normal tasks.

Fast and Deep modes exist for explicit user intent. Fast should optimize for low cost and quick answers. Deep should optimize for hard tasks, verification, and repair loops.

## Supported Model Families

1.0.0 includes first-class model profiles for:

- DeepSeek
- MiMo
- MiniMAX
- Generic OpenAI-compatible providers
- Anthropic-compatible providers

Model profiles should tune prompts and execution policy without adding provider-specific complexity to the main user flow.

## Out Of Scope For 0.x

These capabilities may exist in the repository, but they are not part of the default product surface:

- Complex benchmark UI.
- Always-on planner workflows.
- Always-on sub-agent orchestration.
- Automatic long-term memory persistence.
- Heavy plugin loading during app startup.
- Cross-platform graphical UI.
- Multi-agent research or autonomous background execution.

If reintroduced, each item needs a clear user-facing benefit, a conservative default, and a measurable effect on accuracy, speed, or cost.

## Release Bar

A release is acceptable when:

- `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` passes.
- `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` passes.
- CLI packages are generated for `osx-arm64`, `linux-x64`, and `win-x64`.
- Release packages include sha256 checksums.
- Windows package is smoke-tested locally.
- macOS and Linux packages are smoke-tested before calling a release fully verified.
