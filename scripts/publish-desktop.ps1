param(
    [string[]]$Runtime = @("osx-arm64", "osx-x64", "linux-x64", "win-x64"),
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj"
$outputRootPath = Join-Path $repoRoot $OutputRoot
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Unable to read <Version> from $project"
}
$bundleShortVersion = $version -replace '[-+].*$', ''
$bundleVersion = ($version -replace '[^0-9]', '')
if ([string]::IsNullOrWhiteSpace($bundleVersion)) { $bundleVersion = "1" }

New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

# Per-platform packaging choice: macOS / Windows zip; Linux tar.gz.
$packFormat = @{
    "osx-arm64" = "zip"
    "osx-x64"   = "zip"
    "linux-x64" = "tar.gz"
    "win-x64"   = "zip"
}

foreach ($rid in $Runtime) {
    $artifactName = "aichat-desktop-$rid"
    $publishDir = Join-Path $outputRootPath $artifactName
    $packExt = $packFormat[$rid]
    if (-not $packExt) { $packExt = "zip" }
    $archivePath = Join-Path $outputRootPath "$artifactName.$packExt"

    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    if (Test-Path $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    if (Test-Path "$archivePath.sha256") {
        Remove-Item -LiteralPath "$archivePath.sha256" -Force
    }

    dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir

    Get-ChildItem -Path $publishDir -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force

    # Rename the assembly-named binary to the product name 'aichat' so the
    # CLI surface is consistent across platforms. Self-contained + single
    # file publish ignores AppHostName, so we rename after the fact. The
    # Avalonia XAML avares:// URIs keep the original assembly name —
    # renaming the executable does not affect them.
    $isWindowsTarget = $rid.StartsWith("win-")
    $isMacTarget = $rid.StartsWith("osx-")
    $legacyExe = if ($isWindowsTarget) { "AIChat.App.Avalonia.exe" } else { "AIChat.App.Avalonia" }
    $productExe = if ($isWindowsTarget) { "aichat.exe" } else { "aichat" }
    $legacyPath = Join-Path $publishDir $legacyExe
    $productPath = Join-Path $publishDir $productExe
    if ((Test-Path $legacyPath) -and (-not (Test-Path $productPath))) {
        Move-Item -LiteralPath $legacyPath -Destination $productPath
    }

    if ($isMacTarget) {
        $appDir = Join-Path $publishDir "AIChat.app"
        $contentsDir = Join-Path $appDir "Contents"
        $macOsDir = Join-Path $contentsDir "MacOS"
        $resourcesDir = Join-Path $contentsDir "Resources"
        New-Item -ItemType Directory -Force -Path $macOsDir, $resourcesDir | Out-Null
        Move-Item -LiteralPath $productPath -Destination (Join-Path $macOsDir "aichat")
        $infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>aichat</string>
<key>CFBundleIdentifier</key><string>com.nychy.aichat</string>
<key>CFBundleName</key><string>AIChat</string>
<key>CFBundleDisplayName</key><string>AIChat</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$bundleShortVersion</string>
<key>CFBundleVersion</key><string>$bundleVersion</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
        Set-Content -LiteralPath (Join-Path $contentsDir "Info.plist") -Value $infoPlist -Encoding utf8NoBOM
    }

    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md"), (Join-Path $repoRoot "LICENSE"), (Join-Path $repoRoot "docs/INSTALL.md"), (Join-Path $repoRoot "docs/LAUNCH_PLAN.md") -Destination $publishDir

    if ($isMacTarget) {
        $appBinary = Join-Path $publishDir "AIChat.app/Contents/MacOS/aichat"
        $infoPlistPath = Join-Path $publishDir "AIChat.app/Contents/Info.plist"
        if (-not (Test-Path -LiteralPath $appBinary -PathType Leaf) -or
            -not (Test-Path -LiteralPath $infoPlistPath -PathType Leaf)) {
            throw "Invalid macOS package layout for $rid"
        }
    }
    elseif (-not (Test-Path -LiteralPath $productPath -PathType Leaf)) {
        throw "Published package for $rid does not contain $productExe"
    }

    switch ($packExt) {
        "tar.gz" {
            tar -czf $archivePath -C $publishDir .
        }
        default {
            Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archivePath -Force
        }
    }

    $archiveHash = Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath
    "$($archiveHash.Hash.ToLowerInvariant())  $(Split-Path $archivePath -Leaf)" |
        Set-Content -Path "$archivePath.sha256" -Encoding ascii
    Write-Host "Created $archivePath"
}

$checksumPath = Join-Path $outputRootPath "SHA256SUMS.txt"
Get-ChildItem -Path $outputRootPath -File |
    Where-Object { $_.Name -match '\.(zip|tar\.gz)$' } |
    Sort-Object Name |
    ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
        "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
    } |
    Set-Content -Path $checksumPath -Encoding ascii

Write-Host "Wrote $checksumPath"
