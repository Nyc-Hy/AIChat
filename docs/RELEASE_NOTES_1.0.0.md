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

Validated locally on Windows and macOS Apple Silicon.

### macOS Apple Silicon (M-series, arm64)

- OS: macOS 26.5.2 (Darwin 25.5.0, arm64)
- Runtime: .NET 10.0.8 (self-contained, single-file)
- Build: `dotnet build AIChat.sln` — 0 warnings, 0 errors.
- Tests: `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj` — 626/626 passing on net10.0.
- Artifact: `aichat-cli-osx-arm64.zip` (~33 MB compressed, ~89 MB self-contained binary).
- SHA-256: `2e54a1113e8d471a09bf2f7ada0f156eb3879e74ad041fabf6a7625b1cd4ba53`.
- INSTALL.md flow verified end-to-end:
  - `unzip` + `chmod +x ./aichat` — clean layout, no `__MACOSX` resource forks.
  - `./aichat --version` — `1.0.0+864b1652b5a76e8de0be58dd952093350fb687aa`.
  - `./aichat doctor` — reports .NET 10.0.8, macOS 26.5.2, 15 tools, no provider configured.
  - `./aichat models` — lists all 5 provider profiles (MiMo, DeepSeek, MiniMax, OpenAI-compatible, Anthropic).
  - `./aichat config show` / `config list` — defaults render correctly.
  - `./aichat init --project .` — detects 2 verification commands.
  - `./aichat context "smoke"` — returns project snapshot, file index (430 files), context pack (28 included / 42 omitted, ~1189 tokens).
  - `./aichat config set-provider --provider deepseek --api-key <dummy>` + `config test` — `OpenAICompatibleChatProvider` reaches the real DeepSeek endpoint, parses 401, classifies as `Authentication`.
  - PATH install to `~/.local/bin/aichat` works.

### Windows x64 (validated on a separate machine)

- Build passes with 0 warnings and 0 errors.
- Test suite passes: 564 tests.
- Windows smoke verifies the Avalonia app starts and the CLI package runs `--version`, `doctor`, `models`, `config set-provider`, `config test`, `init`, `projects list`, `context`, and TUI command switching.
- Release packages are generated for `osx-arm64`, `linux-x64`, and `win-x64`.
- Generated checksums match local zip artifacts.

### Known gap

- Linux x64 release package is built but has not been smoke-tested on a real Linux machine yet.
- API keys for configured providers are stored on disk using Windows DPAPI on Windows. On macOS and Linux the current implementation falls back to a "plain" marker (the file is still owned by the user account, but is not encrypted at rest). Tracking encrypted protection for non-Windows platforms as a post-1.0 follow-up.

Before calling a platform fully verified, run the smoke tests from `INSTALL.md` on that platform.
