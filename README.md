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
    ├── official_waves.json
    └── waves/
```

## 平台支持

- `Windows`
  - 当前主支持平台
  - 本地安装脚本、发行版安装器、打包流程都已完整覆盖
- `macOS`
  - 实验性支持
  - 运行时逻辑是托管代码 + 本地 HTTP 控制台，理论上可跨端
  - 目前提供 `.sh` 安装器和构建验证，但还缺少实机长期验证
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

## 构建与安装

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

也可以只做本地构建：

```powershell
.\scripts\build-mod.ps1 -Configuration Release
```

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
  - 安装器会先自动扫描 Steam 库，找不到游戏时再请求用户输入路径
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
