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
$installerShell = Join-Path $PSScriptRoot "release-install-mod.sh"
$manifestPath = Join-Path $repoRoot "manifest.json"
$manifest = Get-Content -Path $manifestPath | ConvertFrom-Json
$version = [string]$manifest.version

function Copy-TextFileWithLf {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $content = [System.IO.File]::ReadAllText($Source, [System.Text.Encoding]::UTF8)
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Destination, $normalized, $utf8NoBom)
}

function Copy-TextFileWithUtf8Bom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $content = [System.IO.File]::ReadAllText($Source, [System.Text.Encoding]::UTF8)
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Destination, $content, $utf8Bom)
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts"
}

if (-not $SkipBuild) {
    & $buildScript -Configuration $Configuration -ReferenceDir $ReferenceDir
}

$binaryDir = Join-Path $repoRoot "bin\$Configuration\net9.0"
$binaryPath = Join-Path $binaryDir "dglab_socket_spire2.dll"
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

if (-not (Test-Path $binaryPath)) {
    throw "Could not find build output '$binaryPath'. Run scripts/build-mod.ps1 first or omit -SkipBuild."
}

Copy-Item $binaryPath $modRoot -Force
Copy-Item (Join-Path $repoRoot "manifest.json") $modRoot -Force
Copy-Item (Join-Path $repoRoot "data\official_waves.json") (Join-Path $modRoot "official_waves.wave") -Force
Copy-Item $installerBatch (Join-Path $packageRoot "install-mod.bat") -Force
Copy-TextFileWithUtf8Bom -Source $installerPowerShell -Destination (Join-Path $packageRoot "install-mod.ps1")
Copy-TextFileWithLf -Source $installerShell -Destination (Join-Path $packageRoot "install-mod.sh")

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath)

Write-Output "Packaged release archive: $zipPath"
