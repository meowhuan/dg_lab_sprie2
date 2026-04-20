param(
    [string]$GameRoot = ""
)

$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modSourceRoot = Join-Path $packageRoot "dglab_socket_spire2"

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
        $inputRoot = Read-Host "无法自动定位《杀戮尖塔 2》。请输入游戏安装目录"
        if (-not [string]::IsNullOrWhiteSpace($inputRoot) -and (Test-Path (Join-Path $inputRoot "SlayTheSpire2.exe"))) {
            return $inputRoot
        }

        Write-Warning "在 '$inputRoot' 下没有找到 SlayTheSpire2.exe。"
    }
}

function Get-SaveAppDataRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        return Join-Path $env:APPDATA "SlayTheSpire2"
    }

    return $null
}

function Get-SaveStorageRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    $saveAppDataRoot = Get-SaveAppDataRoot

    if (-not [string]::IsNullOrWhiteSpace($saveAppDataRoot) -and (Test-Path $saveAppDataRoot)) {
        foreach ($containerName in @("steam", "default")) {
            $containerPath = Join-Path $saveAppDataRoot $containerName
            if (-not (Test-Path $containerPath)) {
                continue
            }

            Get-ChildItem -Path $containerPath -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                $roots.Add($_.FullName)
            }
        }
    }

    return $roots | Select-Object -Unique
}

function Get-SaveProfileMappings {
    $mappings = New-Object System.Collections.Generic.List[object]

    foreach ($saveRoot in Get-SaveStorageRoots) {
        Get-ChildItem -Path $saveRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "profile*" } |
            ForEach-Object {
                $vanillaSaveDir = Join-Path $_.FullName "saves"
                if (Test-Path $vanillaSaveDir) {
                    $mappings.Add([pscustomobject]@{
                        SaveRoot = $saveRoot
                        ProfileName = $_.Name
                        VanillaSaveDir = $vanillaSaveDir
                        ModdedSaveDir = Join-Path $saveRoot "modded\$($_.Name)\saves"
                    })
                }
            }
    }

    return [object[]]$mappings.ToArray()
}

function Test-DirectoryHasItems {
    param(
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return $false
    }

    return (@(Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue).Count -gt 0)
}

function Get-SaveBackupPaths {
    $paths = New-Object System.Collections.Generic.List[string]
    $saveAppDataRoot = Get-SaveAppDataRoot

    if (-not [string]::IsNullOrWhiteSpace($saveAppDataRoot) -and (Test-Path $saveAppDataRoot)) {
        $paths.Add($saveAppDataRoot)
    }

    foreach ($saveRoot in Get-SaveStorageRoots) {
        $paths.Add($saveRoot)
    }

    foreach ($steamRoot in Get-SteamRoots) {
        $userdataRoot = Join-Path $steamRoot "userdata"
        if (-not (Test-Path $userdataRoot)) {
            continue
        }

        Get-ChildItem -Path $userdataRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $steamCloudRoot = Join-Path $_.FullName "2868840"
            if (Test-Path $steamCloudRoot) {
                $paths.Add($steamCloudRoot)
            }
        }
    }

    return $paths | Select-Object -Unique
}

function Get-SaveBackupHints {
    $paths = New-Object System.Collections.Generic.List[string]
    $saveAppDataRoot = Get-SaveAppDataRoot

    if (-not [string]::IsNullOrWhiteSpace($saveAppDataRoot)) {
        $paths.Add($saveAppDataRoot)
        $paths.Add((Join-Path $saveAppDataRoot "default\1"))
        $paths.Add((Join-Path $saveAppDataRoot "steam\<Steam64ID>"))
    }

    foreach ($steamRoot in Get-SteamRoots) {
        $paths.Add((Join-Path $steamRoot "userdata\<Steam3AccountID>\2868840"))
    }

    return $paths | Select-Object -Unique
}

function Show-InstallSafetyNotice {
    $detectedSavePaths = @(Get-SaveBackupPaths)
    $hintSavePaths = @(Get-SaveBackupHints)

    Write-Host ""
    Write-Warning "《杀戮尖塔 2》的原版和 mod 模式使用不同的存档目录。"
    Write-Warning "如果你第一次进入 mod 模式后发现进度像是【不见了】，通常不是丢档，而是原版存档还没有复制到 mod 存档目录。"
    Write-Warning "此安装器可以帮助你准备 mod 存档目录并复制原版存档，但如果游戏之后使用了不同的存档根目录或不同的 Steam 账号，自动迁移仍可能不可用。"
    Write-Host ""
    Write-Host "继续之前："
    Write-Host "  1. 请先彻底关闭《杀戮尖塔 2》。"
    Write-Host "  2. 请先把存档备份到游戏目录和 Steam 目录之外的位置。"

    if ($detectedSavePaths.Count -gt 0) {
        Write-Host "检测到以下存档目录，建议先备份："
        foreach ($path in $detectedSavePaths) {
            Write-Host "  - $path"
        }
    }
    else {
        Write-Host "未检测到现有存档目录，请优先检查这些常见路径："
        foreach ($path in $hintSavePaths) {
            Write-Host "  - $path"
        }
    }

    Write-Host ""
}

function Confirm-BackupDone {
    $backupConfirmation = Read-Host "如已关闭游戏并完成存档备份，请输入 1 继续"
    if ($backupConfirmation -cne "1") {
        throw "安装已取消。请先备份存档，然后重新运行安装器。"
    }
}

function Install-PackagedMod {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedGameRoot
    )

    $modRoot = Join-Path $ResolvedGameRoot "mods\dglab_socket_spire2"

    New-Item -ItemType Directory -Force -Path $modRoot | Out-Null
    Copy-Item (Join-Path $modSourceRoot "*") $modRoot -Recurse -Force

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

    return $modRoot
}

function Test-ModdedEnvironmentEnabled {
    foreach ($saveRoot in Get-SaveStorageRoots) {
        if (Test-Path (Join-Path $saveRoot "modded")) {
            return $true
        }
    }

    return $false
}

function Ensure-ModdedSaveDirectories {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Mappings
    )

    foreach ($mapping in $Mappings) {
        New-Item -ItemType Directory -Force -Path $mapping.ModdedSaveDir | Out-Null
    }
}

function Show-SaveMigrationPaths {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Mappings
    )

    if ($Mappings.Count -eq 0) {
        Write-Warning "没有在 %APPDATA%\SlayTheSpire2 下检测到原版存档配置目录。"
        return
    }

    Write-Host "检测到以下 原版 -> mod 模式 存档路径："
    foreach ($mapping in $Mappings) {
        Write-Host "  - 来源：$($mapping.VanillaSaveDir)"
        Write-Host "    目标：$($mapping.ModdedSaveDir)"
    }
}

function Offer-ModEnvironmentActivation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedGameRoot,

        [Parameter(Mandatory = $true)]
        [object[]]$Mappings
    )

    if (Test-ModdedEnvironmentEnabled) {
        return
    }

    Write-Host ""
    Write-Warning "目前还没有检测到 mod 模式存档目录。"
    Write-Host "下次启动游戏时，游戏应该能在 mods 文件夹里检测到这个 mod。"
    Write-Host "如果游戏弹出 mod 提示，请选择【Load Mods】，游戏应会重启一次并进入 mod 模式。"
    Write-Host "安装器现在可以先为你准备对应的 mod 存档目录，并可选地帮你启动游戏。"

    if ($Mappings.Count -gt 0) {
        Show-SaveMigrationPaths -Mappings $Mappings
        Write-Host "如果游戏之后改用了别的存档根目录或别的 Steam 账号，自动迁移仍可能不可用。"
    }

    Write-Host ""

    $activationChoice = Read-Host "如需现在准备 mod 存档目录并启动游戏，请输入 1；直接回车可跳过"
    if ($activationChoice -cne "1") {
        return
    }

    if ($Mappings.Count -gt 0) {
        Ensure-ModdedSaveDirectories -Mappings $Mappings
    }

    $gameExe = Join-Path $ResolvedGameRoot "SlayTheSpire2.exe"
    if (Test-Path $gameExe) {
        Start-Process -FilePath $gameExe | Out-Null
        Write-Host "游戏已启动。"
        Write-Host "如果出现提示，请选择【Load Mods】，等待游戏重启进入 mod 模式，并至少进入一次主菜单，然后关闭游戏回到安装器。"
        $readyConfirmation = Read-Host "完成上述步骤后请输入 1 继续"
        if ($readyConfirmation -cne "1") {
            Write-Warning "未确认 mod 模式启动成功，安装器将继续后续步骤。"
        }
    }
    else {
        Write-Warning "找不到 '$gameExe'。请手动启动游戏，如有提示请选择【Load Mods】，然后回到下面的存档迁移步骤。"
    }
}

function Get-SaveTransferCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Mappings
    )

    $candidates = New-Object System.Collections.Generic.List[object]

    foreach ($mapping in $Mappings) {
        if (-not (Test-DirectoryHasItems -Path $mapping.VanillaSaveDir)) {
            continue
        }

        if (Test-DirectoryHasItems -Path $mapping.ModdedSaveDir) {
            continue
        }

        $candidates.Add($mapping)
    }

    return [object[]]$candidates.ToArray()
}

function Offer-SaveTransfer {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Mappings
    )

    Write-Host ""

    if ($Mappings.Count -eq 0) {
        Write-Warning "没有检测到原版存档配置，因此安装器无法推断 mod 模式的目标存档路径。"
        return
    }

    Write-Host "原版和 mod 模式的存档进度是分开保存的。"
    Write-Host "如果你希望在 mod 模式里立刻看到原版已经解锁的内容，就需要把原版存档复制到对应的 mod 存档目录。"
    Write-Host "推荐做法是让安装器执行复制。如果你更想手动移动，也可以按下面打印出的同一路径自行处理。"
    Show-SaveMigrationPaths -Mappings $Mappings

    $candidates = @(Get-SaveTransferCandidates -Mappings $Mappings)
    if ($candidates.Count -eq 0) {
        Write-Host "没有找到可用于自动复制的空 mod 存档目录，或缺失的 mod 存档目录。"
        Write-Host "如果 mod 模式里仍然看不到进度，请按上面的路径手动复制或移动存档。"
        return
    }

    Write-Host ""
    $copyChoice = Read-Host "如需现在自动复制原版存档到空的 mod 存档目录，请输入 1；直接回车可跳过"
    if ($copyChoice -cne "1") {
        Write-Host "已跳过自动复制。你仍然可以按上面的路径手动复制或移动存档。"
        return
    }

    foreach ($mapping in $candidates) {
        New-Item -ItemType Directory -Force -Path $mapping.ModdedSaveDir | Out-Null
        Copy-Item (Join-Path $mapping.VanillaSaveDir "*") $mapping.ModdedSaveDir -Recurse -Force
    }

    Write-Host "已将原版存档复制到 $($candidates.Count) 个 mod 存档目录。"
}

if (-not (Test-Path (Join-Path $modSourceRoot "dglab_socket_spire2.dll"))) {
    throw "在 '$modSourceRoot' 下找不到打包后的 mod 文件。"
}

Show-InstallSafetyNotice
Confirm-BackupDone

$resolvedGameRoot = Resolve-GameRoot -ExplicitGameRoot $GameRoot
$modRoot = Install-PackagedMod -ResolvedGameRoot $resolvedGameRoot

$initialMappings = @(Get-SaveProfileMappings)
Offer-ModEnvironmentActivation -ResolvedGameRoot $resolvedGameRoot -Mappings $initialMappings
Offer-SaveTransfer -Mappings @(Get-SaveProfileMappings)

Write-Output "已安装到 $modRoot"
