# Release Checklist

Use this checklist before publishing a public **desktop** release.

## 1.0.0 Beta

### Local validation

- [ ] Run `dotnet build AIChat.sln --no-restore -m:1 -v:minimal`.
- [ ] Run `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` and confirm all tests pass.
- [ ] Run `dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj` locally and confirm the window opens.
- [ ] Run `pwsh scripts/publish-desktop.ps1` to produce the four platform archives under `artifacts/release/`.
- [ ] Verify `artifacts/release/SHA256SUMS.txt` matches every `.zip` and `.tar.gz`.
- [ ] On macOS Apple Silicon: extract `aichat-desktop-osx-arm64.zip`, install the `.app` bundle, and confirm the window opens.
- [ ] On Linux x64: extract `aichat-desktop-linux-x64.tar.gz`, launch the binary, and confirm the window opens.
- [ ] On Windows x64: extract `aichat-desktop-win-x64.zip`, launch `aichat.exe`, and confirm the window opens.
- [ ] End-to-end Avalonia smoke (any platform): add a project folder, configure a provider, run **Test connection**, send a task, approve a tool call, confirm the run completes.

### GitHub release

- [ ] Confirm `CHANGELOG.md` has the release summary.
- [ ] Confirm `docs/RELEASE_NOTES_1.0.0.md` is up to date.
- [ ] Open the PR from `codex/desktop-rebuild`.
- [ ] Push the release branch.
- [ ] Tag the release as `v1.0.0`.
- [ ] Confirm the `Release Desktop` workflow succeeds.
- [ ] Confirm GitHub Release contains:
  - [ ] `aichat-desktop-osx-arm64.zip` + `.sha256`
  - [ ] `aichat-desktop-osx-x64.zip` + `.sha256`
  - [ ] `aichat-desktop-linux-x64.tar.gz` + `.sha256`
  - [ ] `aichat-desktop-win-x64.zip` + `.sha256`

## 1.0.0 Stable (post-Beta)

- [ ] All 1.0.0 Beta items above.
- [ ] Real-provider Avalonia end-to-end on macOS, Linux, and Windows (DeepSeek / MiMo / MiniMAX).
- [ ] Linux x64 release archive smoke-tested on a real machine.
- [ ] macOS Keychain / Linux Secret Service persistence verified; unavailable vaults must show the session-only warning and write no plaintext.
- [ ] Re-run the Avalonia app on each platform with a real coding task: project context → task → tool approval → verification → summary.
