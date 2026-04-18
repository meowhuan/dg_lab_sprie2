param(
    [string]$GameDir = "",

    [string]$TargetDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path $repoRoot "refs\sts2"
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    if (-not [string]::IsNullOrWhiteSpace($env:STS2_REF_DIR)) {
        $GameDir = $env:STS2_REF_DIR
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:GameDir)) {
        $GameDir = $env:GameDir
    }
    else {
        $GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
    }
}

$requiredFiles = @(
    "sts2.dll",
    "GodotSharp.dll",
    "0Harmony.dll"
)

foreach ($file in $requiredFiles) {
    $source = Join-Path $GameDir $file
    if (-not (Test-Path $source)) {
        throw "Missing required reference assembly: $source"
    }
}

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item (Join-Path $GameDir $file) (Join-Path $TargetDir $file) -Force
}

Write-Output "Synced STS2 references to $TargetDir"
