param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ReferenceDir = "",

    [string]$OutputDir = "",

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot "build-mod.ps1"
$installerBatch = Join-Path $PSScriptRoot "release-install-mod.bat"
$installerPowerShell = Join-Path $PSScriptRoot "release-install-mod.ps1"
$manifestPath = Join-Path $repoRoot "manifest.json"
$manifest = Get-Content -Path $manifestPath | ConvertFrom-Json
$version = [string]$manifest.version

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts"
}

if (-not $SkipBuild) {
    & $buildScript -Configuration $Configuration -ReferenceDir $ReferenceDir
}

$binaryDir = Join-Path $repoRoot "bin\$Configuration\net9.0"
$packageRoot = Join-Path $OutputDir "staging"
$modRoot = Join-Path $packageRoot "dglab_socket_spire2"
$zipPath = Join-Path $OutputDir "dglab_socket_spire2-$version.zip"

if (Test-Path $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Force -Path $modRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $modRoot "waves") | Out-Null

Copy-Item (Join-Path $binaryDir "dglab_socket_spire2.dll") $modRoot -Force
Copy-Item (Join-Path $repoRoot "manifest.json") $modRoot -Force
Copy-Item (Join-Path $repoRoot "data\official_waves.json") (Join-Path $modRoot "official_waves.json") -Force
Copy-Item $installerBatch (Join-Path $packageRoot "install-mod.bat") -Force
Copy-Item $installerPowerShell (Join-Path $packageRoot "install-mod.ps1") -Force

$archiveInputs = (Get-ChildItem -LiteralPath $packageRoot -Force | Select-Object -ExpandProperty FullName)
Compress-Archive -Path $archiveInputs -DestinationPath $zipPath -Force

Write-Output "Packaged release archive: $zipPath"
