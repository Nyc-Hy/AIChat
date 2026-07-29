# Pull Request: Prepare CLI 0.5.0 release

Use this content when opening the pull request from `codex/cli-0.5-release` into `main`.

## Title

Prepare CLI 0.5.0 release

## Body

```markdown
## Summary

- add the cross-platform `aichat` CLI and lightweight TUI beta for the 0.5.0 product path
- add Fast / Standard / Deep execution modes plus first-pass DeepSeek, MiMo, and MiniMAX model profiles
- simplify default agent startup behavior so heavyweight planner, sub-agent, memory, benchmark, and plugin paths stay out of the default flow
- add local publish tooling, GitHub release workflow, checksums, release notes, release checklist, launch plan, and product scope docs

## Validation

- `dotnet build AIChat.sln --no-restore -m:1 -v:minimal`
- `dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1 -v:minimal`
- `pwsh scripts\publish-cli.ps1`
- Windows release smoke: `--version`, `models --provider deepseek`, `config set-provider`, `init`, `projects list`, `doctor`, TUI command switching
- verified generated `SHA256SUMS.txt` against all three zip packages

## Notes

macOS `osx-arm64` and Linux `linux-x64` packages are generated and structurally verified from Windows, but still need true host smoke tests before the release is called fully verified.
```

## URL

https://github.com/Nyc-Hy/AIChat/pull/new/codex/cli-0.5-release
