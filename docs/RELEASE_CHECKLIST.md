# Release Checklist

Use this checklist before publishing a public CLI release.

## 0.5.0

### Local validation

- [ ] Run `dotnet build AIChat.sln --no-restore -m:1 -v:minimal`.
- [ ] Run `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal`.
- [ ] Run `pwsh scripts/publish-cli.ps1`.
- [ ] Verify `artifacts/release/SHA256SUMS.txt`.
- [ ] Run `artifacts/release/aichat-cli-win-x64/aichat.exe --version`.
- [ ] Run `artifacts/release/aichat-cli-win-x64/aichat.exe doctor`.

### Cross-platform smoke tests

On macOS Apple Silicon:

- [ ] Extract `aichat-cli-osx-arm64.zip`.
- [ ] Run `./aichat --version`.
- [ ] Run `./aichat doctor`.
- [ ] Run `./aichat tui --project <repo>`.

On Linux x64:

- [ ] Extract `aichat-cli-linux-x64.zip`.
- [ ] Run `./aichat --version`.
- [ ] Run `./aichat doctor`.
- [ ] Run `./aichat tui --project <repo>`.

### GitHub release

- [ ] Confirm `CHANGELOG.md` has the release summary.
- [ ] Confirm `docs/RELEASE_NOTES_0.5.0.md` is up to date.
- [ ] Push the release branch.
- [ ] Tag the release as `v0.5.0`.
- [ ] Confirm the `Release CLI` workflow succeeds.
- [ ] Confirm GitHub Release contains:
  - [ ] `aichat-cli-osx-arm64.zip`
  - [ ] `aichat-cli-osx-arm64.sha256`
  - [ ] `aichat-cli-linux-x64.zip`
  - [ ] `aichat-cli-linux-x64.sha256`
  - [ ] `aichat-cli-win-x64.zip`
  - [ ] `aichat-cli-win-x64.sha256`
