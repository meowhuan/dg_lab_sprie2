param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ReferenceDir = "",

    [string]$Project = "dg_lab_socket_spire2.csproj"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = if ([System.IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $repoRoot $Project }
$outputDir = Join-Path $repoRoot "bin\$Configuration\net9.0"
$outputDll = Join-Path $outputDir "dglab_socket_spire2.dll"
$dotnetDownloadUrl = "https://aka.ms/dotnet-download"

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

function Get-TargetFramework {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    try {
        [xml]$projectXml = Get-Content -LiteralPath $ProjectPath
        $targetFramework = $projectXml.Project.PropertyGroup.TargetFramework | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($targetFramework)) {
            return [string]$targetFramework
        }
    }
    catch {
    }

    return ""
}

function Get-RequiredSdkMajorVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework
    )

    if ($TargetFramework -match '^net(\d+)\.0$') {
        return [int]$matches[1]
    }

    return $null
}

function New-DotnetSdkInstallMessage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Summary
    )

    return "$Summary`n下载地址：`n$dotnetDownloadUrl"
}

function Initialize-DotnetCliEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $dotnetCliHome = Join-Path $RepositoryRoot ".tools\dotnet-cli"
    $nugetPackages = Join-Path $RepositoryRoot ".tools\nuget-packages"

    New-Item -ItemType Directory -Force -Path $dotnetCliHome | Out-Null
    New-Item -ItemType Directory -Force -Path $nugetPackages | Out-Null

    $env:DOTNET_CLI_HOME = $dotnetCliHome
    $env:NUGET_PACKAGES = $nugetPackages
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
}

function Assert-DotnetSdkAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw (New-DotnetSdkInstallMessage -Summary "未检测到 dotnet 命令。构建此项目需要安装 .NET SDK 9.0 或更高版本。")
    }

    $targetFramework = Get-TargetFramework -ProjectPath $ProjectPath
    $requiredMajor = Get-RequiredSdkMajorVersion -TargetFramework $targetFramework
    $sdkLines = & dotnet --list-sdks

    if ($LASTEXITCODE -ne 0) {
        throw (New-DotnetSdkInstallMessage -Summary "无法查询当前已安装的 .NET SDK。请安装 .NET SDK 9.0 或更高版本后重试。")
    }

    $installedMajors = @(
        $sdkLines |
            ForEach-Object {
                if ($_ -match '^(\d+)\.') {
                    [int]$matches[1]
                }
            } |
            Sort-Object -Unique
    )

    $highestInstalledMajor = if ($installedMajors.Count -gt 0) {
        ($installedMajors | Measure-Object -Maximum).Maximum
    }
    else {
        $null
    }

    if ($requiredMajor -and ($null -eq $highestInstalledMajor -or $highestInstalledMajor -lt $requiredMajor)) {
        $installedSummary = if ($sdkLines) {
            "当前已安装 SDK：$($sdkLines -join ', ')"
        }
        else {
            "当前未检测到任何 .NET SDK。"
        }

        throw (New-DotnetSdkInstallMessage -Summary "此项目目标框架为 $targetFramework，需要 .NET SDK $requiredMajor.0 或更高版本。$installedSummary")
    }
}

$resolvedReferenceDir = Resolve-Sts2ReferenceDir -ExplicitReferenceDir $ReferenceDir -RepositoryRoot $repoRoot
Write-Host "Using STS2 reference directory: $resolvedReferenceDir"

Initialize-DotnetCliEnvironment -RepositoryRoot $repoRoot
Assert-DotnetSdkAvailable -ProjectPath $projectPath

& dotnet build $projectPath -c $Configuration "/p:Sts2ReferenceDir=$resolvedReferenceDir"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

if (-not (Test-Path $outputDll)) {
    throw "Build completed without producing '$outputDll'."
}
