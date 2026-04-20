# DG-LAB Socket Control for Slay the Spire 2

这个 mod 会在游戏里启动一个 DG-LAB `socket/v2` 兼容桥接服务，并把杀戮尖塔2的战斗/奖励事件映射成 DG-LAB 强度或波形指令。

## 输出结构

构建输出：

```text
mods/
└── dglab_socket_spire2/
    ├── dglab_socket_spire2.cfg
    ├── manifest.json
    └── dglab_socket_spire2.dll
```

发行版压缩包结构：

```text
dglab_socket_spire2-x.y.z.zip
├── install-mod.bat
├── install-mod.ps1
├── install-mod.sh
└── dglab_socket_spire2/
    ├── dglab_socket_spire2.dll
    ├── manifest.json
    ├── official_waves.wave
    └── waves/
```

## 平台支持

- `Windows`
  - 当前主支持平台
  - 本地安装脚本、发行版安装器、打包流程都已完整覆盖
- `macOS`
  - 实验性支持
  - 已在 Apple Silicon macOS 上完成构建、安装、游戏加载和本地控制台访问验证
  - 安装器会把 mod 放到 `SlayTheSpire2.app/Contents/MacOS/mods/`，这是 STS2 macOS 版本实际扫描的 mod 目录
  - 运行时逻辑是托管代码 + 本地 HTTP 控制台，仍建议不同 macOS / Steam 环境继续做实机验证
- `Linux / SteamOS`
  - 实验性支持
  - 安装器按 Steam Linux 库路径做自动索引
  - SteamOS 视为 Linux 路径变体处理，目前同样属于实验性支持

## 当前实现

- 使用 STS2 官方 `ModInitializer` + Harmony 装载。
- 事件来自 `MegaCrit.Sts2.Core.Hooks.Hook`。
- 本地启动 TCP/HTTP/WebSocket 一体化桥接服务。
- mod 自己作为前端 client 连接到本地服务，APP 扫码后绑定到该 client。
- 游戏里只保留入口按钮：
  - 主菜单里的 `DG-LAB 控制台`
  - Mod 页面里的 `打开 DG-LAB 控制台`
- 主要控制界面已经切换为独立控制台页面，而不是游戏内弹窗。

## 使用方式

1. 启动游戏，让 mod 完成本地桥接服务启动。
2. 在主菜单或 Mod 页面点击 `DG-LAB 控制台`。
3. 浏览器会打开本地控制台：
   - `http://127.0.0.1:9999/control`
4. 如果要给 DG-LAB APP 配对，打开：
   - `http://127.0.0.1:9999/pair`

控制台里当前有两条主工作流：

- `全局设置`
  - 默认打开
  - 用于调整全局启用、安全选项、低血量阈值、全局冷却、手动覆盖配对地址等
- `事件规则`
  - 上半部是事件速改表，适合批量改开关、触发模式、通道和冷却
  - 下半部是单事件精调区，适合修改波形、强度参数、恢复延迟等细项

## 控制台功能

当前独立控制台支持：

- 查看服务端 / 前端 / 绑定状态
- 查看 A/B 通道强度和最近事件通知
- 显示配对二维码
- 切换当前预设
- 保存全局设置
- 批量速改事件规则
- 单事件详细编辑
- 发送测试波形
- 发送测试强度
- 清空双通道
- 重载配置与波形库

## 默认规则说明

- 新建或缺省事件规则的默认触发模式是 `强度 + 波形`
- 默认控制台页签是 `全局设置`
- 事件规则页里支持把当前详细规则复制到勾选事件，便于快速套用一组参数

## 本地接口

控制台本身就是基于本地 HTTP API 驱动的，主要入口如下：

- `GET /pair`
- `GET /control`
- `GET /api/status`
- `GET /api/control/state`
- `GET /api/control/global/save?...`
- `GET /api/control/preset?...`
- `GET /api/control/rule/save?...`
- `GET /api/control/test-wave?...`
- `GET /api/control/test-strength?...`
- `GET /api/control/clear`

默认端口是 `9999`，也就是：

- `http://127.0.0.1:9999/pair`
- `http://127.0.0.1:9999/control`

## 配置

首次运行会在 mod 目录生成 `dglab_socket_spire2.cfg`。

- `server.publicHost` 为空时，mod 自动挑选一个局域网 IPv4。
- 如果自动识别到的是 VPN / 虚拟网卡地址，手动改成你手机能访问到的真实局域网地址。
- `safety.globalEnabled` 控制全局总开关。
- `safety.ignoreEventsWhileUnbound` 控制未绑定 APP 时是否忽略事件。
- `safety.autoClearOnCombatEnd` / `safety.autoClearOnDisconnect` 控制自动清空行为。
- `safety.globalCooldownMs` 控制全局事件冷却。
- `safety.lowHealthThresholdPercent` 控制低血量事件阈值。
- `currentPreset` 可选：
  - `Balanced`
  - `CombatHeavy`
  - `RewardHeavy`
  - `Minimal`

## 波形文件

内置波形随发行包安装为 `official_waves.wave`。自定义波形请放在 mod 目录下的 `waves/` 子目录中，并使用 `.wave` 扩展名。

`.wave` 文件内容仍然是 JSON，扩展名改为 `.wave` 是为了避免 STS2 的 mod loader 把非 manifest 的 `.json` 文件误当作 mod manifest 扫描。

## 构建与安装

前置要求：

- 已安装 `.NET SDK 9.0` 或更高版本
  - 下载地址：`https://aka.ms/dotnet-download`
- 仓库内存在 `refs/sts2/` 引用 DLL，或通过 `STS2_REF_DIR` / `-ReferenceDir` 指向有效引用目录

```powershell
.\scripts\install-mod.ps1
```

本地安装脚本会先自动扫描 Steam 库里的 `Slay the Spire 2`，找不到时再请求手动输入游戏目录。

如果你已经知道游戏目录，也可以显式指定：

```powershell
.\scripts\install-mod.ps1 -GameRoot "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

macOS / Linux / SteamOS 也可以使用 shell 安装脚本：

```bash
./scripts/install-mod.sh
```

或显式指定游戏目录：

```bash
./scripts/install-mod.sh --game-root "/path/to/steamapps/common/Slay the Spire 2"
```

如果游戏正在运行，Windows 会锁住 `dglab_socket_spire2.dll`，需要先退出游戏再覆盖安装。

发行版安装器说明：

- STS2 的 vanilla 和 modded 使用不同的存档目录。首次进入 modded 模式后，如果解锁内容看起来“不见了”，通常不是丢档，而是原版存档还没复制到 modded 存档目录。
- 发行版 `install-mod.bat` / `install-mod.ps1` / `install-mod.sh` 现在会先提示备份风险，并要求用户先备份存档。
- 安装器会优先列出本机检测到的备份目录；如果没检测到，会回退显示常见位置。
  - Windows 常见位置包括 `%APPDATA%\SlayTheSpire2`、`%APPDATA%\SlayTheSpire2\default\1`、`%APPDATA%\SlayTheSpire2\steam\<Steam64ID>` 和 `Steam\userdata\<Steam3AccountID>\2868840`
  - macOS / Linux / SteamOS 会提示各自常见的本地用户目录或 Steam `userdata/2868840` 路径
- 安装器会把 mod 文件复制到游戏目录，然后：
  - 如果还没检测到 modded 存档目录，会询问是否现在辅助开启 mod 环境
  - 选择后，安装器会先准备匹配的 modded 存档目录，再尝试启动游戏
  - 用户需要在游戏里选择 `Load Mods`，让游戏重启一次并至少进一次主菜单
- 安装器随后会列出它检测到的 `vanilla -> modded` 存档路径，并询问是否自动复制原版存档到空的 modded 存档目录。
  - 推荐自动复制，原版存档会保留
  - 如果你更想“移动”而不是“复制”，安装器也会把源路径和目标路径打印出来，方便你自己操作
- 自动迁移是按当前检测到的本地存档根目录和 Steam 账号目录推断的；如果游戏后续选择了别的存档根或别的 Steam 账号，自动迁移可能不会生效。

也可以只做本地构建：

```powershell
.\scripts\build-mod.ps1 -Configuration Release
```

如果机器上没有可用的 .NET SDK，脚本现在会直接报出前置要求并停止，不会继续进入误导性的复制失败报错。

或打出发行版压缩包：

```powershell
.\scripts\package-release.ps1 -Configuration Release
```

如果要把本机游戏目录里的引用 DLL 同步到仓库内的 `refs/sts2/`，可以执行：

```powershell
.\scripts\sync-sts2-refs.ps1
```

现在 `refs/sts2/` 可以直接提交到仓库，用于 GitHub Actions 构建。
如果本机游戏版本更新了，建议重新执行一次同步脚本，把引用 DLL 刷到最新。

## GitHub Actions

仓库已经包含两个工作流：

- `.github/workflows/ci.yml`
  - 用于常规 CI 构建、打包并上传带版本号的 artifact
  - 会额外在 Windows / macOS / Linux 上做构建验证
- `.github/workflows/release.yml`
  - 用于 tag 发布版构建，并在 tag push 时自动附加 zip 到 GitHub Release
  - 发行版 zip 根目录会附带 `install-mod.bat`、`install-mod.ps1` 和 `install-mod.sh`
  - 安装器会先提示存档路径分离与备份风险，并要求用户确认已备份存档
  - 安装器会复制 mod 文件、尝试辅助开启 mod 环境，并在需要时提示用户在游戏里选择 `Load Mods`
  - 安装器会列出 `vanilla -> modded` 存档路径，并询问是否自动复制原版存档到 modded 存档目录
  - 之后安装器会自动扫描 Steam 库，找不到游戏时再请求用户输入路径
  - 会校验 Git tag 与 `manifest.json` 的版本一致，约定 tag 形如 `v0.1.0`

注意：GitHub 托管 runner 不会自带 STS2 的引用 DLL，因此需要把以下文件放到仓库内的 `refs/sts2/`：

- `refs/sts2/sts2.dll`
- `refs/sts2/GodotSharp.dll`
- `refs/sts2/0Harmony.dll`

工作流和本地构建脚本都会优先使用 `refs/sts2/`，其次使用显式传入的 `Sts2ReferenceDir` / `STS2_REF_DIR`，最后才退回本机默认 Steam 安装路径。

推荐发布流程：

1. 执行 `.\scripts\sync-sts2-refs.ps1`，刷新仓库内的引用 DLL。
2. 更新 `manifest.json` 里的版本号。
3. 提交代码和 `refs/sts2/` 里的引用 DLL。
4. 打 `v<manifest.version>` 形式的 tag，例如 `v0.1.0`。
5. `release.yml` 会自动构建发行版 zip、上传 artifact，并把 zip 附加到 GitHub Release。

注意：当前 macOS / Linux / SteamOS 支持仍建议在发行说明和文档里保持“实验性”标识，直到至少完成一轮实机安装、启动和控制链路验证。

## 说明

配对页里的二维码图片目前使用在线二维码图片服务生成；如果图片加载失败，仍可直接复制配对链接给 DG-LAB APP 使用。
