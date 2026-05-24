param(
    [string[]]$Runtime = @("osx-arm64", "linux-x64", "win-x64"),
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/AIChat.Cli/AIChat.Cli.csproj"
$outputRootPath = Join-Path $repoRoot $OutputRoot

New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

foreach ($rid in $Runtime) {
    $artifactName = "aichat-cli-$rid"
    $publishDir = Join-Path $outputRootPath $artifactName
    $zipPath = Join-Path $outputRootPath "$artifactName.zip"

    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir

    Get-ChildItem -Path $publishDir -Filter "*.pdb" -File | Remove-Item -Force
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "Created $zipPath"
}

$checksumPath = Join-Path $outputRootPath "SHA256SUMS.txt"
Get-ChildItem -Path $outputRootPath -Filter "aichat-cli-*.zip" -File |
    Sort-Object Name |
    ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
        "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
    } |
    Set-Content -Path $checksumPath -Encoding ascii

Write-Host "Created $checksumPath"
