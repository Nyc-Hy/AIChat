# Release Checklist

Use this checklist before publishing a public CLI release.

## 1.0.0

### Local validation

- [ ] Run `dotnet build AIChat.sln --no-restore -m:1 -v:minimal`.
- [ ] Run `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal`.
- [ ] Run `pwsh scripts/publish-cli.ps1`.
- [ ] Verify `artifacts/release/SHA256SUMS.txt`.
- [ ] Run `artifacts/release/aichat-cli-win-x64/aichat.exe --version`.
- [ ] Run `artifacts/release/aichat-cli-win-x64/aichat.exe doctor`.
- [ ] Run `artifacts/release/aichat-cli-win-x64/aichat.exe context "project overview" --project <repo>`.
- [ ] Run `aichat tui`, then `/context`, `/status`, `/exit`.
- [ ] Verify every zip contains `README.md`, `LICENSE`, `CLI_REFERENCE.md`, `INSTALL.md`, and `LAUNCH_PLAN.md`.

### Cross-platform smoke tests

On macOS Apple Silicon:

- [ ] Extract `aichat-cli-osx-arm64.zip`.
- [ ] Run `./aichat --version`.
- [ ] Run `./aichat doctor`.
- [ ] Run `./aichat context "project overview" --project <repo>`.
- [ ] Run `./aichat tui --project <repo>`.

On Linux x64:

- [ ] Extract `aichat-cli-linux-x64.zip`.
- [ ] Run `./aichat --version`.
- [ ] Run `./aichat doctor`.
- [ ] Run `./aichat context "project overview" --project <repo>`.
- [ ] Run `./aichat tui --project <repo>`.

### GitHub release

- [ ] Confirm `CHANGELOG.md` has the release summary.
- [ ] Confirm `docs/RELEASE_NOTES_1.0.0.md` is up to date.
- [ ] Open the PR from `codex/cli-1.0-roadmap`.
- [ ] Push the release branch.
- [ ] Tag the release as `v1.0.0`.
- [ ] Confirm the `Release CLI` workflow succeeds.
- [ ] Confirm GitHub Release contains:
  - [ ] `aichat-cli-osx-arm64.zip`
  - [ ] `aichat-cli-osx-arm64.sha256`
  - [ ] `aichat-cli-linux-x64.zip`
  - [ ] `aichat-cli-linux-x64.sha256`
  - [ ] `aichat-cli-win-x64.zip`
  - [ ] `aichat-cli-win-x64.sha256`
