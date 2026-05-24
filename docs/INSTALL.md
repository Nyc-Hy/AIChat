# Install AIChat CLI

AIChat CLI is distributed as self-contained release archives for macOS, Linux, and Windows.

## Download

Download the archive for your platform from the GitHub Release page:

- `aichat-cli-osx-arm64.zip` for Apple Silicon macOS
- `aichat-cli-linux-x64.zip` for Linux x64
- `aichat-cli-win-x64.zip` for Windows x64

Each release also includes sha256 files for verification.

## macOS Apple Silicon

```bash
unzip aichat-cli-osx-arm64.zip -d aichat
cd aichat
chmod +x ./aichat
./aichat --version
./aichat doctor
```

Optional PATH install:

```bash
mkdir -p ~/.local/bin
cp ./aichat ~/.local/bin/aichat
aichat --version
```

If macOS Gatekeeper blocks the binary, allow it from System Settings, or remove quarantine after you verify the checksum:

```bash
xattr -d com.apple.quarantine ./aichat
```

## Linux x64

```bash
unzip aichat-cli-linux-x64.zip -d aichat
cd aichat
chmod +x ./aichat
./aichat --version
./aichat doctor
```

Optional PATH install:

```bash
mkdir -p ~/.local/bin
cp ./aichat ~/.local/bin/aichat
aichat --version
```

## Windows x64

Extract `aichat-cli-win-x64.zip`, then run:

```powershell
.\aichat.exe --version
.\aichat.exe doctor
```

Optional PATH install:

```powershell
$installDir = "$env:LOCALAPPDATA\AIChat\bin"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item .\aichat.exe $installDir -Force
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$installDir", "User")
```

Open a new terminal and run:

```powershell
aichat --version
```

## Configure A Provider

DeepSeek example:

```bash
aichat config set-provider --provider deepseek --api-key "$DEEPSEEK_API_KEY" --model deepseek-chat
aichat config test
```

Then initialize a project:

```bash
cd /path/to/repo
aichat init --project .
aichat context "summarize the project"
aichat tui --project .
```

## Verify Checksums

macOS/Linux:

```bash
shasum -a 256 aichat-cli-osx-arm64.zip
cat aichat-cli-osx-arm64.sha256
```

Windows:

```powershell
Get-FileHash -Algorithm SHA256 .\aichat-cli-win-x64.zip
Get-Content .\aichat-cli-win-x64.sha256
```
