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
shasum -a 256 aichat-desktop-osx-arm64.zip
cat aichat-desktop-osx-arm64.sha256

# Unpack
unzip aichat-desktop-osx-arm64.zip -d AIChat.app.d
# The .app bundle is inside the archive; move it to /Applications
mv AIChat.app.d/AIChat.app /Applications/AIChat.app

# First launch: macOS Gatekeeper will block an unsigned binary the first time
xattr -d com.apple.quarantine /Applications/AIChat.app 2>/dev/null || true
open /Applications/AIChat.app
```

If macOS still blocks the app, allow it from **System Settings → Privacy & Security**, then re-open.

## Linux x64

```bash
shasum -a 256 aichat-desktop-linux-x64.tar.gz
cat aichat-desktop-linux-x64.sha256

tar -xzf aichat-desktop-linux-x64.tar.gz
# The unpacked tree contains aichat (ELF executable) plus README/LICENSE.
# Run it directly, or install:
mkdir -p ~/.local/bin ~/.local/share/applications ~/.local/share/icons
mv aichat ~/.local/bin/aichat-desktop
cat > ~/.local/share/applications/aichat.desktop <<'EOF'
[Desktop Entry]
Type=Application
Name=AIChat
Exec=aichat-desktop %u
Icon=aichat
Terminal=false
Categories=Development;IDE;
EOF
```

## Windows x64

```powershell
# Verify checksum
Get-FileHash -Algorithm SHA256 .\aichat-desktop-win-x64.zip
Get-Content .\aichat-desktop-win-x64.sha256

# Unpack
Expand-Archive .\aichat-desktop-win-x64.zip -DestinationPath .\AIChat

# Optional: install to user-local app data
$installDir = "$env:LOCALAPPDATA\AIChat"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item .\AIChat\* $installDir -Recurse -Force
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$installDir", "User")
```

Open a new terminal and run `aichat`.

## First-Run Setup

The first time the app starts it will show the **Avalonia main window** with the project sidebar empty. The onboarding flow covers:

1. Pick a project folder (the **Add project** button on the sidebar).
2. Configure a model provider (the **Advanced** expander on the right rail — API key, model, base URL).
3. Run a smoke test (the **Test connection** button next to the provider config).
4. Send a task in the bottom input box.

Project settings and provider credentials are persisted to:

| Platform | Path |
|---|---|
| macOS | `~/Library/Application Support/AIChat/` |
| Linux | `~/.config/AIChat/` |
| Windows | `%APPDATA%\AIChat\` |

## Verifying Checksums

macOS / Linux:

```bash
shasum -a 256 aichat-desktop-osx-arm64.zip
cat aichat-desktop-osx-arm64.sha256
```

Windows:

```powershell
Get-FileHash -Algorithm SHA256 .\aichat-desktop-win-x64.zip
Get-Content .\aichat-desktop-win-x64.sha256
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
