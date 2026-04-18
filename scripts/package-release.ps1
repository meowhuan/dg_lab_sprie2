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

    $content = [System.IO.File]::ReadAllText($Source)
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Destination, $normalized, $utf8NoBom)
}

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
Copy-TextFileWithLf -Source $installerShell -Destination (Join-Path $packageRoot "install-mod.sh")

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath)

Write-Output "Packaged release archive: $zipPath"
