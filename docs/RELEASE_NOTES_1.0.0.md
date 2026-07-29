# AIChat v1.0.0 Release Notes

AIChat v1.0.0 is the first stable Avalonia-first release of the project, with CLI/TUI kept as the secondary terminal interface.

The release focuses on a practical Claude Code style workflow for third-party coding models: accurate task execution, fast feedback, low token usage, explicit tool approval, and visible context diagnostics.

## Highlights

- Stable cross-platform Avalonia desktop UI.
- Supported cross-platform `aichat` CLI.
- Supported interactive `aichat tui`.
- `aichat context` for model-free context diagnostics before spending tokens.
- TUI `/context [goal]` for in-session context diagnostics.
- `aichat config test` for provider readiness checks.
- Fast / Standard / Deep execution modes.
- First-class DeepSeek, MiMo, and MiniMAX model profiles.
- Conservative default agent behavior: single-agent loop, planner off by default, sub-agents off by default, auto-fix off by default, memory writes off by default.
- Explicit approval for write, shell, build/test, and git mutation tools.
- macOS Apple Silicon, Linux x64, and Windows x64 release archives.
- SHA-256 checksum files for release artifacts.

## Stable Command Surface

- `aichat --version`
- `aichat doctor`
- `aichat models`
- `aichat config show`
- `aichat config list`
- `aichat config set-provider`
- `aichat config use`
- `aichat config test`
- `aichat init`
- `aichat projects list`
- `aichat context`
- `aichat ask`
- `aichat tui`

## TUI Commands

- `/help`
- `/mode fast|standard|deep`
- `/context [goal]`
- `/yes`
- `/plain`
- `/no-write`
- `/verify`
- `/status`
- `/exit`

## Verification

Validated locally on Windows:

- Build passes with 0 warnings and 0 errors.
- Test suite passes: 564 tests.
- Windows smoke verifies the Avalonia app starts and the CLI package runs `--version`, `doctor`, `models`, `config set-provider`, `config test`, `init`, `projects list`, `context`, and TUI command switching.
- Release packages are generated for `osx-arm64`, `linux-x64`, and `win-x64`.
- Generated checksums match local zip artifacts.

Before calling a platform fully verified, run the smoke tests from `INSTALL.md` on that platform.
