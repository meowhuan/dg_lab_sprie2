param(
    [string]$ReferenceDir = "",
    [string]$GameRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot "build-mod.ps1"

function Get-SteamRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    foreach ($registryPath in @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam"
    )) {
        try {
            $item = Get-ItemProperty -Path $registryPath -ErrorAction Stop
            foreach ($candidate in @($item.SteamPath, $(if ($item.SteamExe) { Split-Path $item.SteamExe -Parent }))) {
                if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                    $roots.Add($candidate)
                }
            }
        }
        catch {
        }
    }

    foreach ($fallback in @(
        "C:\Program Files (x86)\Steam",
        "C:\Program Files\Steam"
    )) {
        $roots.Add($fallback)
    }

    return $roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
}

function Resolve-GameRootFromLibraries {
    foreach ($steamRoot in Get-SteamRoots) {
        $libraryRoots = New-Object System.Collections.Generic.List[string]
        $libraryRoots.Add($steamRoot)

        $libraryVdf = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (Test-Path $libraryVdf) {
            foreach ($line in Get-Content $libraryVdf) {
                if ($line -match '"path"\s*"([^"]+)"') {
                    $libraryRoots.Add(($matches[1] -replace '\\\\', '\'))
                }
                elseif ($line -match '^\s*"[0-9]+"\s*"([^"]+)"') {
                    $libraryRoots.Add(($matches[1] -replace '\\\\', '\'))
                }
            }
        }

        foreach ($libraryRoot in ($libraryRoots | Select-Object -Unique)) {
            $candidate = Join-Path $libraryRoot "steamapps\common\Slay the Spire 2"
            if (Test-Path (Join-Path $candidate "SlayTheSpire2.exe")) {
                return $candidate
            }
        }
    }

    return $null
}

function Resolve-GameRoot {
    param(
        [string]$ExplicitGameRoot
    )

    foreach ($candidate in @(
        $ExplicitGameRoot,
        $env:STS2_GAME_DIR,
        $env:GameRoot
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path (Join-Path $candidate "SlayTheSpire2.exe"))) {
            return $candidate
        }
    }

    $detected = Resolve-GameRootFromLibraries
    if (-not [string]::IsNullOrWhiteSpace($detected)) {
        return $detected
    }

    while ($true) {
        $inputRoot = Read-Host "Could not locate Slay the Spire 2 automatically. Enter the game install directory"
        if (-not [string]::IsNullOrWhiteSpace($inputRoot) -and (Test-Path (Join-Path $inputRoot "SlayTheSpire2.exe"))) {
            return $inputRoot
        }

        Write-Warning "SlayTheSpire2.exe was not found under '$inputRoot'."
    }
}

& $buildScript -Configuration Release -ReferenceDir $ReferenceDir

$resolvedGameRoot = Resolve-GameRoot -ExplicitGameRoot $GameRoot
$modRoot = Join-Path $resolvedGameRoot "mods\dglab_socket_spire2"
$outputDir = Join-Path $repoRoot "bin\Release\net9.0"
$outputDll = Join-Path $outputDir "dglab_socket_spire2.dll"

if (-not (Test-Path $outputDll)) {
    throw "Could not find build output '$outputDll'. The build step did not produce the mod assembly."
}

New-Item -ItemType Directory -Force -Path $modRoot | Out-Null
Copy-Item $outputDll $modRoot -Force
Copy-Item (Join-Path $repoRoot "manifest.json") $modRoot -Force
Remove-Item (Join-Path $modRoot "official_waves.json") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $repoRoot "data\official_waves.json") (Join-Path $modRoot "official_waves.wave") -Force
New-Item -ItemType Directory -Force -Path (Join-Path $modRoot "waves") | Out-Null

$legacyConfig = Join-Path $modRoot "config.json"
$newConfig = Join-Path $modRoot "dglab_socket_spire2.cfg"
if (Test-Path $legacyConfig) {
    if (-not (Test-Path $newConfig)) {
        Move-Item $legacyConfig $newConfig -Force
    }
    else {
        Remove-Item $legacyConfig -Force
    }
}

Write-Output "Installed to $modRoot"
