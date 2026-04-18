param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ReferenceDir = "",

    [string]$Project = "dg_lab_socket_spire2.csproj"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = if ([System.IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $repoRoot $Project }

function Test-Sts2ReferenceDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Test-Path (Join-Path $Path "sts2.dll")) `
        -and (Test-Path (Join-Path $Path "GodotSharp.dll")) `
        -and (Test-Path (Join-Path $Path "0Harmony.dll"))
}

function Resolve-Sts2ReferenceDir {
    param(
        [string]$ExplicitReferenceDir,
        [string]$RepositoryRoot
    )

    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($ExplicitReferenceDir)) {
        $candidates.Add($ExplicitReferenceDir)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:STS2_REF_DIR)) {
        $candidates.Add($env:STS2_REF_DIR)
    }

    $candidates.Add((Join-Path $RepositoryRoot "refs\sts2"))

    if (-not [string]::IsNullOrWhiteSpace($env:GameDir)) {
        $candidates.Add($env:GameDir)
    }

    $candidates.Add("C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64")

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $resolved = try {
            (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path
        }
        catch {
            $candidate
        }

        if (Test-Sts2ReferenceDir -Path $resolved) {
            return $resolved
        }
    }

    throw "Could not find STS2 reference assemblies. Set STS2_REF_DIR, pass -ReferenceDir, or place sts2.dll/GodotSharp.dll/0Harmony.dll under refs\sts2."
}

$resolvedReferenceDir = Resolve-Sts2ReferenceDir -ExplicitReferenceDir $ReferenceDir -RepositoryRoot $repoRoot
Write-Host "Using STS2 reference directory: $resolvedReferenceDir"

& dotnet build $projectPath -c $Configuration "/p:Sts2ReferenceDir=$resolvedReferenceDir"
