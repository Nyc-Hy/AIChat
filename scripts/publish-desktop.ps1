param(
    [string[]]$Runtime = @("osx-arm64", "osx-x64", "linux-x64", "win-x64"),
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj"
$outputRootPath = Join-Path $repoRoot $OutputRoot

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
    $legacyExe = if ($IsWindows -or $env:OS -eq "Windows_NT") { "AIChat.App.Avalonia.exe" } else { "AIChat.App.Avalonia" }
    $productExe = if ($IsWindows -or $env:OS -eq "Windows_NT") { "aichat.exe" } else { "aichat" }
    $legacyPath = Join-Path $publishDir $legacyExe
    $productPath = Join-Path $publishDir $productExe
    if ((Test-Path $legacyPath) -and (-not (Test-Path $productPath))) {
        Move-Item -LiteralPath $legacyPath -Destination $productPath
    }

    switch ($packExt) {
        "tar.gz" {
            tar -czf $archivePath -C $publishDir .
        }
        default {
            Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archivePath -Force
        }
    }

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
