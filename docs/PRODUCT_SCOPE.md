# AIChat Product Scope

AIChat is a Vibe Coding assistant optimised for third-party coding models — DeepSeek, MiMo, MiniMAX, OpenAI-compatible providers, and Anthropic-compatible providers.

The product goal is not to expose every agent framework capability. The goal is a small, reliable, low-cost coding workbench that can replace a Claude Code style workflow for day-to-day repository work.

The detailed product baseline is in [PRODUCT_BASELINE.md](PRODUCT_BASELINE.md).

## Primary Interface

The primary — and only — product surface is the **cross-platform Avalonia desktop application** for macOS, Linux, and Windows. The desktop UI owns:

- project selection and conversation history
- provider configuration and connection testing
- model-free context diagnostics
- the agent loop, including tool approval and verification
- run history and session metrics

The legacy CLI / TUI surface has been removed. Automation, scripting, and keyboard-driven workflows are reached through the desktop app or by hosting `AIChat.Application` from a .NET program.

## Core Priorities

1. Clear and simple Avalonia UI.
2. Explicit project and conversation boundaries.
3. Accurate task execution.
4. Fast response time.
5. Low token usage.
6. Accurate context-size visibility.
7. Per-session input / output / cache usage summaries.
8. Better prompt and provider-cache hit rates.
9. Predictable tool approval and write behaviour.
10. Simple install and release process.

## Default Product Shape

Defaults stay conservative:

- Standard execution mode by default.
- Planner disabled by default.
- Sub-agents disabled by default.
- Auto-fix disabled by default.
- Memory writes disabled unless explicitly requested.
- Tool rounds capped tightly for normal tasks.

Fast and Deep modes exist for explicit user intent. Fast optimises for low cost and quick answers. Deep optimises for hard tasks, verification, and repair loops.

## Supported Model Families

1.0.0 includes first-class model profiles for:

- DeepSeek
- MiMo
- MiniMAX
- Generic OpenAI-compatible providers
- Anthropic-compatible providers

Model profiles tune prompts and execution policy without adding provider-specific complexity to the main user flow.

## Out Of Scope For 0.x / 1.0

These capabilities may exist in the repository, but they are not part of the default product surface:

- Complex benchmark UI.
- Always-on planner workflows.
- Always-on sub-agent orchestration.
- Automatic long-term memory persistence.
- Heavy plugin loading during app startup.
- CLI / TUI as a separate product surface.
- Multi-agent research or autonomous background execution.

If reintroduced, each item needs a clear user-facing benefit, a conservative default, and a measurable effect on accuracy, speed, or cost.

## Release Bar

A release is acceptable when:

- `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` passes.
- `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` passes.
- Avalonia desktop package is generated for `osx-arm64`, `linux-x64`, and `win-x64`.
- Release packages include SHA-256 checksums.
- Windows package is smoke-tested locally.
- macOS and Linux packages are smoke-tested before a release is called fully verified.
