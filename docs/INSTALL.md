# Install AIChat Desktop

AIChat 1.0 Beta is distributed as a cross-platform Avalonia desktop application for macOS, Linux, and Windows.

The legacy CLI / TUI surface has been removed. If you need a scripted entry point, drive the desktop app from the OS launcher or use the `AIChat.Application` libraries from your own .NET host.

## Requirements

- macOS 12+ on Apple Silicon (`osx-arm64`) or Intel (`osx-x64`)
- Linux x64 (glibc-based, e.g. Ubuntu 22.04+, Debian 12+, Fedora 39+)
- Windows 10 1809+ / Windows 11 on x64

Runtime dependencies are bundled — no .NET SDK install is required for end users.

## Download

Download the archive for your platform from the GitHub Release page:

- `aichat-desktop-osx-arm64.zip` — Apple Silicon macOS
- `aichat-desktop-osx-x64.zip` — Intel macOS
- `aichat-desktop-linux-x64.tar.gz` — Linux x64
- `aichat-desktop-win-x64.zip` — Windows x64

Each release also includes SHA-256 checksum files for verification.

## macOS (Apple Silicon)

```bash
# Verify checksum
shasum -a 256 -c aichat-desktop-osx-arm64.zip.sha256

# Unpack — the archive contains a real AIChat.app bundle,
# plus README / LICENSE / INSTALL / LAUNCH_PLAN.
unzip aichat-desktop-osx-arm64.zip -d aichat-desktop
chmod +x ./aichat-desktop/AIChat.app/Contents/MacOS/aichat

# Optional: install to /Applications
ditto ./aichat-desktop/AIChat.app /Applications/AIChat.app
ln -sf /Applications/AIChat.app/Contents/MacOS/aichat /usr/local/bin/aichat

# First launch: macOS Gatekeeper will block an unsigned binary the first time
xattr -dr com.apple.quarantine /Applications/AIChat.app 2>/dev/null || true
open -a /Applications/AIChat.app
```

If macOS still blocks the app, allow it from **System Settings → Privacy & Security**, then re-launch.

## Linux x64

```bash
sha256sum -c aichat-desktop-linux-x64.tar.gz.sha256

mkdir -p aichat-desktop
tar -xzf aichat-desktop-linux-x64.tar.gz -C aichat-desktop
# The archive contains a single self-contained ELF binary named 'aichat'
# plus README / LICENSE / INSTALL / LAUNCH_PLAN.
chmod +x ./aichat-desktop/aichat

# Optional: install to user-local bin and register a .desktop entry
mkdir -p ~/.local/bin ~/.local/share/applications
mv ./aichat-desktop/aichat ~/.local/bin/aichat
cat > ~/.local/share/applications/aichat.desktop <<'EOF'
[Desktop Entry]
Type=Application
Name=AIChat
Exec=aichat %u
Terminal=false
Categories=Development;IDE;
EOF
```

## Windows x64

```powershell
# Verify checksum
Get-FileHash -Algorithm SHA256 .\aichat-desktop-win-x64.zip
Get-Content .\aichat-desktop-win-x64.zip.sha256

# Unpack — the archive contains aichat.exe (self-contained PE binary)
# plus README / LICENSE / INSTALL / LAUNCH_PLAN.
Expand-Archive .\aichat-desktop-win-x64.zip -DestinationPath .\aichat-desktop

# Optional: install to user-local app data
$installDir = "$env:LOCALAPPDATA\AIChat\bin"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item .\aichat-desktop\aichat.exe $installDir -Force
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$installDir", "User")
```

Open a new terminal and run `aichat`.

## First-Run Setup

The first time the app starts it will show the **Avalonia main window** with the project sidebar empty. The onboarding flow covers:

1. Pick a project folder (the **Add project** button on the sidebar).
2. Open **Settings** and configure a model provider (API key, model, base URL).
3. Run a smoke test with **Test connection** in Settings.
4. Send a task in the bottom input box.

Project settings and provider credentials are persisted to:

| Platform | Path |
|---|---|
| macOS | `~/Library/Application Support/AIChat/` |
| Linux | `~/.config/AIChat/` |
| Windows | `%APPDATA%\AIChat\` |

API keys use Windows DPAPI, macOS Keychain, or Linux Secret Service. If the
platform credential store is unavailable, AIChat keeps the key only for the
current process, shows a warning, and never falls back to plaintext storage.
On a normal launch AIChat restores saved provider credentials once per process
and keeps an in-memory cache for later refreshes. That initial restore is why
macOS may show a Keychain access prompt; repeated F5 refreshes do not need
another vault read.

For a demo, screenshot pass, or automated UI trial, use a separate profile that
never reads the production settings or system credential vault:

```bash
AICHAT_ISOLATED_DATA_ROOT="$(mktemp -d)" \
  dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj
```

`AICHAT_ISOLATED_DATA_ROOT` must be an absolute path. In this mode all app data
and attachments stay under that root, and API keys are session-only.

## Verifying Checksums

macOS:

```bash
shasum -a 256 -c aichat-desktop-osx-arm64.zip.sha256
```

Linux:

```bash
sha256sum -c aichat-desktop-linux-x64.tar.gz.sha256
```

Windows:

```powershell
Get-FileHash -Algorithm SHA256 .\aichat-desktop-win-x64.zip
Get-Content .\aichat-desktop-win-x64.zip.sha256
```

## Build From Source

If you want to build a desktop bundle yourself:

```bash
git clone https://github.com/Nyc-Hy/AIChat.git
cd AIChat
dotnet build src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj -c Release
dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj
```

To produce a self-contained zip for your current platform:

```bash
dotnet publish src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=embedded
```
