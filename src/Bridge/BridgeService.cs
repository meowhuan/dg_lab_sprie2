using System.Reflection;
using System.Text;
using DgLabSocketSpire2.Configuration;
using Godot;
using MegaCrit.Sts2.Core.Rooms;

namespace DgLabSocketSpire2.Bridge;

public sealed class BridgeService
{
    private readonly object _gate = new();
    private readonly ConfigManager _configManager = new();
    private DgLabTcpServer? _server;
    private DgLabFrontendClient? _frontend;
    private ModConfig _config = new();
    private string _publicHost = "127.0.0.1";
    private long _globalLastEventTicks;
    private readonly Dictionary<BridgeEventType, long> _eventCooldowns = new();
    private readonly Dictionary<ChannelRef, long> _strengthGeneration = new();
    private bool _combatActive;
    private bool _lowHealthActive;
    private string _lastEvent = string.Empty;
    private string _uiNotice = string.Empty;
    private bool _hotkeyCaptureActive;
    private long _hotkeyTestDeadlineTicks;

    public static BridgeService Instance { get; } = new();

    private BridgeService()
    {
    }

    public void Start()
    {
        lock (_gate)
        {
            _config = _configManager.Load();
            WaveLibrary.Initialize();
            _publicHost = LanHostResolver.ResolvePreferredHost(_config.Server.PublicHost);
            _server = new DgLabTcpServer(this, _config.Server);
            _server.Start();
            _frontend = new DgLabFrontendClient(this);
            _frontend.Start(_config.Server.Port);

            if (OperatingSystem.IsMacOS())
            {
                _uiNotice = "当前运行平台为 macOS；此平台支持仍属实验性。";
                ModLog.Warn("macOS support is experimental. Verify install, startup, and control flow carefully.");
            }
            else if (OperatingSystem.IsLinux())
            {
                _uiNotice = "当前运行平台为 Linux/SteamOS；此平台支持仍属实验性。";
                ModLog.Warn("Linux/SteamOS support is experimental. Verify install, startup, and control flow carefully.");
            }
        }
    }

    public void OpenPairPage()
    {
        var status = GetStatusSnapshot();
        if (!string.IsNullOrWhiteSpace(status.PairPageUrl))
        {
            OS.ShellOpen(status.PairPageUrl);
        }
    }

    public void OpenControlPanel()
    {
        var status = GetStatusSnapshot();
        if (!string.IsNullOrWhiteSpace(status.ControlPanelUrl))
        {
            OS.ShellOpen(status.ControlPanelUrl);
        }
    }

    public void ReloadConfig()
    {
        lock (_gate)
        {
            _config = _configManager.Load();
            WaveLibrary.Reload();
            _publicHost = LanHostResolver.ResolvePreferredHost(_config.Server.PublicHost);
            _uiNotice = "配置与波形库已重载。";
        }
    }

    public void SaveSettings()
    {
        lock (_gate)
        {
            _configManager.Save();
            _uiNotice = "设置已保存。";
        }
    }

    public string GetCurrentPresetDescription()
    {
        lock (_gate)
        {
            return _config.Presets.TryGetValue(_config.CurrentPreset, out var preset)
                ? preset.Description
                : string.Empty;
        }
    }

    public void SetPreset(string presetName)
    {
        lock (_gate)
        {
            if (!_config.Presets.ContainsKey(presetName))
            {
                return;
            }

            _config.CurrentPreset = presetName;
            _configManager.Update(config => config.CurrentPreset = presetName);
        }
    }

    public IReadOnlyCollection<string> GetPresetNames()
    {
        lock (_gate)
        {
            return _config.Presets.Keys.OrderBy(name => name).ToArray();
        }
    }

    public string GetCurrentPreset()
    {
        lock (_gate)
        {
            return _config.CurrentPreset;
        }
    }

    public ControlPanelState GetControlPanelState()
    {
        lock (_gate)
        {
            var presets = _config.Presets
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new ControlPanelPresetOption
                {
                    Key = entry.Key,
                    DisplayName = GetPresetDisplayName(entry.Key, entry.Value),
                    Description = entry.Value.Description
                })
                .ToList();

            var events = Enum.GetValues<BridgeEventType>()
                .Select(static eventType => new ControlPanelEventOption
                {
                    Key = eventType.ToString(),
                    DisplayName = GetEventDisplayName(eventType)
                })
                .ToList();

            var rules = Enum.GetValues<BridgeEventType>()
                .Select(eventType => new ControlPanelRuleEntry
                {
                    EventType = eventType.ToString(),
                    Rule = CloneRule(_config.Presets.TryGetValue(_config.CurrentPreset, out var preset)
                        && preset.Rules.TryGetValue(eventType, out var rule)
                            ? rule
                            : new EventRuleConfig())
                })
                .ToList();

            return new ControlPanelState
            {
                Status = GetStatusSnapshot(),
                GlobalSettings = new ControlPanelGlobalSettings
                {
                    GlobalEnabled = _config.Safety.GlobalEnabled,
                    IgnoreEventsWhileUnbound = _config.Safety.IgnoreEventsWhileUnbound,
                    AutoClearOnCombatEnd = _config.Safety.AutoClearOnCombatEnd,
                    AutoClearOnDisconnect = _config.Safety.AutoClearOnDisconnect,
                    GlobalCooldownMs = _config.Safety.GlobalCooldownMs,
                    LowHealthThresholdPercent = _config.Safety.LowHealthThresholdPercent,
                    PublicHostOverride = _config.Server.PublicHost
                },
                Presets = presets,
                Events = events,
                Rules = rules,
                Waves = WaveLibrary.Names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
    }

    public string GetHotkeyDisplayText()
    {
        lock (_gate)
        {
            return FormatHotkey(_config.Ui.ToggleHotkey);
        }
    }

    public bool IsHotkeyCaptureActive()
    {
        lock (_gate)
        {
            return _hotkeyCaptureActive;
        }
    }

    public bool IsHotkeyTestActive()
    {
        lock (_gate)
        {
            return _hotkeyTestDeadlineTicks > System.Environment.TickCount64;
        }
    }

    public HotkeyConfig GetHotkeyConfig()
    {
        lock (_gate)
        {
            return new HotkeyConfig
            {
                Key = _config.Ui.ToggleHotkey.Key,
                Ctrl = _config.Ui.ToggleHotkey.Ctrl,
                Alt = _config.Ui.ToggleHotkey.Alt,
                Shift = _config.Ui.ToggleHotkey.Shift
            };
        }
    }

    public void BeginHotkeyCapture()
    {
        lock (_gate)
        {
            _hotkeyCaptureActive = true;
            _uiNotice = "热键录制中：请按下新的快捷键。";
            UiLog.Info("Hotkey capture armed.");
        }
    }

    public void CancelHotkeyCapture()
    {
        lock (_gate)
        {
            _hotkeyCaptureActive = false;
            _uiNotice = "热键录制已取消。";
            UiLog.Info("Hotkey capture cancelled.");
        }
    }

    public void BeginHotkeyTest()
    {
        lock (_gate)
        {
            _hotkeyTestDeadlineTicks = System.Environment.TickCount64 + 10_000;
            _uiNotice = $"热键测试已开始：10 秒内按 {FormatHotkey(_config.Ui.ToggleHotkey)}。";
            UiLog.Info($"Hotkey test armed for {FormatHotkey(_config.Ui.ToggleHotkey)}.");
        }
    }

    public bool CompleteHotkeyCapture(InputEventKey keyEvent)
    {
        lock (_gate)
        {
            if (!_hotkeyCaptureActive)
            {
                UiLog.Warn("Ignored hotkey capture input because capture mode is not active.");
                return false;
            }

            if (!TryGetStableKeyName(keyEvent, out var keyName))
            {
                _uiNotice = "无法识别该热键，请换一个按键。";
                UiLog.Warn($"Rejected hotkey capture input: key={keyEvent.Keycode}, physical={keyEvent.PhysicalKeycode}");
                return true;
            }

            var ctrl = keyEvent.CtrlPressed;
            var alt = keyEvent.AltPressed;
            var shift = keyEvent.ShiftPressed;

            _config.Ui.ToggleHotkey = new HotkeyConfig
            {
                Key = keyName,
                Ctrl = ctrl,
                Alt = alt,
                Shift = shift
            };

            var savedKey = new HotkeyConfig
            {
                Key = keyName,
                Ctrl = ctrl,
                Alt = alt,
                Shift = shift
            };

            _configManager.Update(config => config.Ui.ToggleHotkey = savedKey);
            _hotkeyCaptureActive = false;
            _uiNotice = $"热键已更新为：{FormatHotkey(_config.Ui.ToggleHotkey)}";
            UiLog.Info($"Hotkey updated to {FormatHotkey(_config.Ui.ToggleHotkey)}.");
            return true;
        }
    }

    public bool TryConsumeHotkeyTestPress()
    {
        lock (_gate)
        {
            var now = System.Environment.TickCount64;
            if (_hotkeyTestDeadlineTicks <= now)
            {
                UiLog.Warn("Hotkey test press ignored because the test window has expired.");
                return false;
            }

            _hotkeyTestDeadlineTicks = 0;
            _uiNotice = $"热键测试成功：{FormatHotkey(_config.Ui.ToggleHotkey)}";
            UiLog.Info($"Hotkey test succeeded for {FormatHotkey(_config.Ui.ToggleHotkey)}.");
            return true;
        }
    }

    public int GetConfiguredTestStrength(ChannelRef channel)
    {
        lock (_gate)
        {
            return channel == ChannelRef.A ? _config.Ui.TestStrengthA : _config.Ui.TestStrengthB;
        }
    }

    public void UpdateGlobalSettings(ControlPanelGlobalSettings settings)
    {
        lock (_gate)
        {
            _config.Safety.GlobalEnabled = settings.GlobalEnabled;
            _config.Safety.IgnoreEventsWhileUnbound = settings.IgnoreEventsWhileUnbound;
            _config.Safety.AutoClearOnCombatEnd = settings.AutoClearOnCombatEnd;
            _config.Safety.AutoClearOnDisconnect = settings.AutoClearOnDisconnect;
            _config.Safety.GlobalCooldownMs = Math.Clamp(settings.GlobalCooldownMs, 0, 60_000);
            _config.Safety.LowHealthThresholdPercent = Math.Clamp(settings.LowHealthThresholdPercent, 1, 100);
            _config.Server.PublicHost = settings.PublicHostOverride?.Trim() ?? string.Empty;
            _publicHost = LanHostResolver.ResolvePreferredHost(_config.Server.PublicHost);

            var snapshot = new ControlPanelGlobalSettings
            {
                GlobalEnabled = _config.Safety.GlobalEnabled,
                IgnoreEventsWhileUnbound = _config.Safety.IgnoreEventsWhileUnbound,
                AutoClearOnCombatEnd = _config.Safety.AutoClearOnCombatEnd,
                AutoClearOnDisconnect = _config.Safety.AutoClearOnDisconnect,
                GlobalCooldownMs = _config.Safety.GlobalCooldownMs,
                LowHealthThresholdPercent = _config.Safety.LowHealthThresholdPercent,
                PublicHostOverride = _config.Server.PublicHost
            };

            _configManager.Update(config =>
            {
                config.Safety.GlobalEnabled = snapshot.GlobalEnabled;
                config.Safety.IgnoreEventsWhileUnbound = snapshot.IgnoreEventsWhileUnbound;
                config.Safety.AutoClearOnCombatEnd = snapshot.AutoClearOnCombatEnd;
                config.Safety.AutoClearOnDisconnect = snapshot.AutoClearOnDisconnect;
                config.Safety.GlobalCooldownMs = snapshot.GlobalCooldownMs;
                config.Safety.LowHealthThresholdPercent = snapshot.LowHealthThresholdPercent;
                config.Server.PublicHost = snapshot.PublicHostOverride;
            });

            _uiNotice = "全局设置已更新。";
        }
    }

    public WindowGeometryConfig GetControlDialogGeometry()
    {
        lock (_gate)
        {
            return new WindowGeometryConfig
            {
                HasSavedGeometry = _config.Ui.ControlDialog.HasSavedGeometry,
                X = _config.Ui.ControlDialog.X,
                Y = _config.Ui.ControlDialog.Y,
                Width = _config.Ui.ControlDialog.Width,
                Height = _config.Ui.ControlDialog.Height
            };
        }
    }

    public void SaveControlDialogGeometry(Vector2I position, Vector2I size)
    {
        lock (_gate)
        {
            var width = Math.Max(0, size.X);
            var height = Math.Max(0, size.Y);
            if (width == 0 || height == 0)
            {
                return;
            }

            var geometry = _config.Ui.ControlDialog;
            if (geometry.HasSavedGeometry
                && geometry.X == position.X
                && geometry.Y == position.Y
                && geometry.Width == width
                && geometry.Height == height)
            {
                return;
            }

            geometry.HasSavedGeometry = true;
            geometry.X = position.X;
            geometry.Y = position.Y;
            geometry.Width = width;
            geometry.Height = height;

            var snapshot = new WindowGeometryConfig
            {
                HasSavedGeometry = true,
                X = position.X,
                Y = position.Y,
                Width = width,
                Height = height
            };

            _configManager.Update(config => config.Ui.ControlDialog = snapshot);
            UiLog.Info($"Control dialog geometry saved: pos=({position.X}, {position.Y}), size=({width}, {height})");
        }
    }

    public void SetConfiguredTestStrength(ChannelRef channel, int value)
    {
        lock (_gate)
        {
            var clamped = Math.Clamp(value, 0, 200);
            if (channel == ChannelRef.A)
            {
                _config.Ui.TestStrengthA = clamped;
                _configManager.Update(config => config.Ui.TestStrengthA = clamped);
            }
            else
            {
                _config.Ui.TestStrengthB = clamped;
                _configManager.Update(config => config.Ui.TestStrengthB = clamped);
            }

            _uiNotice = $"{(channel == ChannelRef.A ? "A" : "B")} 通道测试值已设为 {clamped}";
        }
    }

    public void ToggleEnabled()
    {
        lock (_gate)
        {
            var enabled = !_config.Safety.GlobalEnabled;
            _config.Safety.GlobalEnabled = enabled;
            _configManager.Update(config => config.Safety.GlobalEnabled = enabled);
        }
    }

    public bool IsEnabled()
    {
        lock (_gate)
        {
            return _config.Safety.GlobalEnabled;
        }
    }

    public IReadOnlyList<(BridgeEventType EventType, string DisplayName)> GetEditableEvents()
    {
        return Enum.GetValues<BridgeEventType>()
            .Select(static eventType => (eventType, GetEventDisplayName(eventType)))
            .ToArray();
    }

    public EventRuleConfig GetRuleSnapshot(BridgeEventType eventType)
    {
        lock (_gate)
        {
            if (_config.Presets.TryGetValue(_config.CurrentPreset, out var preset)
                && preset.Rules.TryGetValue(eventType, out var rule))
            {
                return CloneRule(rule);
            }

            return new EventRuleConfig();
        }
    }

    public void SaveRule(BridgeEventType eventType, EventRuleConfig updatedRule)
    {
        lock (_gate)
        {
            if (!_config.Presets.TryGetValue(_config.CurrentPreset, out var preset))
            {
                return;
            }

            NormalizeRule(updatedRule);
            preset.Rules[eventType] = CloneRule(updatedRule);
            var presetName = _config.CurrentPreset;
            var ruleCopy = CloneRule(updatedRule);
            _configManager.Update(config =>
            {
                if (!config.Presets.TryGetValue(presetName, out var editablePreset))
                {
                    return;
                }

                editablePreset.Rules[eventType] = ruleCopy;
            });
            _uiNotice = $"{GetEventDisplayName(eventType)} 规则已保存。";
        }
    }

    public IReadOnlyCollection<string> GetAvailableWaveNames()
    {
        return WaveLibrary.Names;
    }

    public async Task SendTestPulseAsync(string waveName, ChannelRef channel)
    {
        var frontend = _frontend;
        if (frontend == null)
        {
            return;
        }

        await frontend.SendWaveAsync(channel, WaveLibrary.GetFrames(waveName), 1);
    }

    public async Task SendTestStrengthAsync(ChannelRef channel, int value)
    {
        var frontend = _frontend;
        if (frontend == null)
        {
            return;
        }

        await frontend.SendStrengthAsync(channel, StrengthOperation.Set, value);
    }

    public async Task ClearAllAsync()
    {
        var frontend = _frontend;
        if (frontend == null)
        {
            return;
        }

        await frontend.ClearChannelAsync(ChannelRef.A);
        await frontend.ClearChannelAsync(ChannelRef.B);
        lock (_gate)
        {
            _uiNotice = "已下发 A/B 双通道清空。";
        }
    }

    public void NotifyServerStateChanged()
    {
    }

    public void NotifyFrontendConnectionChanged()
    {
    }

    public void NotifyFrontendMessageReceived()
    {
    }

    public BridgeStatus GetStatusSnapshot()
    {
        lock (_gate)
        {
            var frontend = _frontend;
            var clientId = frontend?.ClientId;
            var pairSocketUrl = string.IsNullOrWhiteSpace(clientId)
                ? null
                : $"ws://{_publicHost}:{_config.Server.Port}/{clientId}";
            var pairUrl = string.IsNullOrWhiteSpace(pairSocketUrl)
                ? null
                : $"https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#{pairSocketUrl}";
            var pairQrUrl = string.IsNullOrWhiteSpace(pairUrl)
                ? null
                : $"https://api.qrserver.com/v1/create-qr-code/?size=320x320&data={Uri.EscapeDataString(pairUrl)}";

            var presetDescription = _config.Presets.TryGetValue(_config.CurrentPreset, out var preset)
                ? preset.Description
                : string.Empty;
            var frontendNotice = frontend?.LastNotice ?? string.Empty;
            var mergedNotice = !string.IsNullOrWhiteSpace(_uiNotice) ? _uiNotice : frontendNotice;

            return new BridgeStatus
            {
                GlobalEnabled = _config.Safety.GlobalEnabled,
                ServerRunning = _server?.IsRunning ?? false,
                FrontendConnected = frontend?.IsConnected ?? false,
                IsBound = frontend?.IsBound ?? false,
                PublicHost = _publicHost,
                Port = _config.Server.Port,
                PairPageUrl = $"http://127.0.0.1:{_config.Server.Port}/pair",
                ControlPanelUrl = $"http://127.0.0.1:{_config.Server.Port}/control",
                FrontendClientId = clientId,
                TargetId = frontend?.TargetId,
                PairSocketUrl = pairSocketUrl,
                PairQrUrl = pairQrUrl,
                CurrentPreset = _config.CurrentPreset,
                CurrentPresetDescription = presetDescription,
                CurrentHotkey = FormatHotkey(_config.Ui.ToggleHotkey),
                HotkeyCaptureActive = _hotkeyCaptureActive,
                HotkeyTestActive = _hotkeyTestDeadlineTicks > System.Environment.TickCount64,
                ConfiguredTestStrengthA = _config.Ui.TestStrengthA,
                ConfiguredTestStrengthB = _config.Ui.TestStrengthB,
                AppStrengthA = frontend?.StrengthA ?? 0,
                AppStrengthB = frontend?.StrengthB ?? 0,
                AppLimitA = frontend?.LimitA ?? 0,
                AppLimitB = frontend?.LimitB ?? 0,
                LastFeedback = frontend?.LastFeedback ?? string.Empty,
                LastError = frontend?.LastError ?? string.Empty,
                LastNotice = mergedNotice,
                LastEvent = _lastEvent,
                TotalConnections = _server?.ConnectionCount ?? 0,
                TotalPairs = _server?.PairCount ?? 0
            };
        }
    }

    public string BuildPairPageHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>DG-LAB Pairing</title>
  <style>
    :root { color-scheme: dark; --bg:#111; --panel:#1a1a1a; --text:#f4f0e8; --muted:#b5aea1; --accent:#c65a2e; --line:#2a2a2a; }
    * { box-sizing:border-box; }
    body { margin:0; font-family: "Segoe UI", "PingFang SC", sans-serif; background:radial-gradient(circle at top, #25211b, #111 65%); color:var(--text); }
    main { max-width:920px; margin:0 auto; padding:28px 18px 40px; display:grid; gap:18px; }
    .card { background:rgba(26,26,26,.92); border:1px solid var(--line); border-radius:18px; padding:18px; box-shadow:0 18px 60px rgba(0,0,0,.35); }
    h1 { margin:0 0 8px; font-size:30px; }
    p { margin:0; color:var(--muted); line-height:1.5; }
    .grid { display:grid; grid-template-columns: 340px 1fr; gap:18px; align-items:start; }
    .qr { display:grid; justify-items:center; gap:12px; }
    .qr img { width:320px; height:320px; background:#fff; border-radius:12px; border:8px solid #fff; }
    code, pre { font-family: Consolas, "Cascadia Code", monospace; white-space:pre-wrap; word-break:break-all; }
    .pill { display:inline-block; padding:6px 10px; border-radius:999px; background:#2d241f; color:#f2ceb5; border:1px solid #6a3d23; }
    .row { display:grid; gap:8px; margin-top:12px; }
    .status { display:grid; gap:8px; margin-top:12px; }
    .status div { padding:10px 12px; border-radius:12px; background:#141414; border:1px solid var(--line); }
    a { color:#ffb08a; }
    @media (max-width: 820px) { .grid { grid-template-columns: 1fr; } .qr img { width:280px; height:280px; } }
  </style>
</head>
<body>
  <main>
    <section class="card">
      <h1>DG-LAB SOCKET 配对页</h1>
      <p>此页面由杀戮尖塔2 mod 本地生成。APP 扫码后会连接到当前电脑上的 DG-LAB WebSocket 服务。</p>
    </section>
    <section class="grid">
      <div class="card qr">
        <img id="qr" alt="Pair QR" />
        <span class="pill" id="bindState">等待前端连接</span>
      </div>
      <div class="card">
        <div class="row">
          <strong>当前配对链接</strong>
          <code id="pairUrl">尚未生成 clientId</code>
        </div>
        <div class="row">
          <strong>前端 ID</strong>
          <code id="clientId">-</code>
        </div>
        <div class="row">
          <strong>APP ID</strong>
          <code id="targetId">-</code>
        </div>
        <div class="row">
          <strong>服务状态</strong>
          <div class="status">
            <div id="serverState">Server: -</div>
            <div id="frontendState">Frontend: -</div>
            <div id="pairState">Pair: -</div>
            <div id="noticeState">Notice: -</div>
          </div>
        </div>
        <div class="row">
          <strong>说明</strong>
          <p>如果二维码图片没有加载出来，仍然可以直接复制上面的完整链接。APP 识别规则与 DG-LAB 官方文档一致。</p>
        </div>
      </div>
    </section>
  </main>
  <script>
    async function refresh() {
      const response = await fetch('/api/status', { cache: 'no-store' });
      const status = await response.json();
      document.getElementById('clientId').textContent = status.frontendClientId || '-';
      document.getElementById('targetId').textContent = status.targetId || '-';
      document.getElementById('pairUrl').textContent = status.pairSocketUrl
        ? 'https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#' + status.pairSocketUrl
        : '尚未生成 clientId';
      document.getElementById('bindState').textContent = status.isBound ? '已绑定 APP' : (status.frontendConnected ? '等待 APP 扫码' : '等待前端连接');
      document.getElementById('serverState').textContent = `Server: ${status.serverRunning ? 'running' : 'stopped'} @ ${status.publicHost}:${status.port}`;
      document.getElementById('frontendState').textContent = `Frontend: ${status.frontendConnected ? 'connected' : 'disconnected'} (${status.currentPreset})`;
      document.getElementById('pairState').textContent = `Pair: ${status.isBound ? 'bound' : 'idle'}, connections=${status.totalConnections}, pairs=${status.totalPairs}`;
      document.getElementById('noticeState').textContent = `Notice: ${status.lastNotice || status.lastError || status.lastEvent || '-'}`;
      if (status.pairQrUrl) {
        document.getElementById('qr').src = status.pairQrUrl;
      }
    }
    refresh();
    setInterval(refresh, 1000);
  </script>
</body>
</html>
""";

        return html;
    }

    public string BuildControlPageHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>DG-LAB 控制台</title>
  <style>
    :root {
      color-scheme: dark;
      --bg:#101318;
      --panel:#171c24;
      --panel-2:#1e2530;
      --text:#f4f6fb;
      --muted:#a3afc2;
      --accent:#f09b66;
      --accent-2:#c76631;
      --line:#344152;
      --good:#7ed18a;
      --warn:#ffcf73;
      --danger:#ff8f8f;
      --shadow:0 20px 60px rgba(0,0,0,.28);
    }
    * { box-sizing:border-box; }
    body {
      margin:0;
      font-family:"Segoe UI","Microsoft YaHei UI","PingFang SC",sans-serif;
      color:var(--text);
      background:
        radial-gradient(circle at top right, rgba(240,155,102,.15), transparent 28%),
        radial-gradient(circle at bottom left, rgba(96,143,255,.12), transparent 24%),
        linear-gradient(180deg, #101318, #0c1014 100%);
    }
    button, select, input {
      font:inherit;
      color:var(--text);
      background:var(--panel-2);
      border:1px solid var(--line);
      border-radius:12px;
      padding:10px 12px;
    }
    button {
      cursor:pointer;
      background:linear-gradient(180deg, #2c3646, #222b38);
    }
    button.primary {
      background:linear-gradient(180deg, var(--accent), var(--accent-2));
      border-color:#f0b08a;
      color:#fffaf6;
      font-weight:600;
    }
    button:disabled { opacity:.6; cursor:not-allowed; }
    main {
      min-height:100vh;
      display:grid;
      grid-template-rows:auto 1fr;
      gap:18px;
      padding:20px;
    }
    .topbar {
      display:flex;
      justify-content:space-between;
      align-items:center;
      gap:16px;
      padding:18px 22px;
      border:1px solid var(--line);
      border-radius:20px;
      background:rgba(23,28,36,.92);
      box-shadow:var(--shadow);
    }
    .title h1 { margin:0; font-size:28px; }
    .title p { margin:6px 0 0; color:var(--muted); }
    .toolbar { display:flex; flex-wrap:wrap; gap:10px; }
    .layout {
      display:grid;
      grid-template-columns: 380px minmax(0, 1fr);
      gap:18px;
      min-height:0;
    }
    .sidebar, .editor {
      min-height:0;
      display:flex;
      flex-direction:column;
      gap:18px;
    }
    .panel {
      border:1px solid var(--line);
      border-radius:20px;
      background:rgba(23,28,36,.96);
      box-shadow:var(--shadow);
      overflow:hidden;
    }
    .panel-header {
      padding:16px 18px 0;
    }
    .panel-header h2 {
      margin:0;
      color:var(--accent);
      font-size:18px;
    }
    .panel-header p {
      margin:8px 0 0;
      color:var(--muted);
      line-height:1.5;
      font-size:14px;
    }
    .panel-body {
      padding:18px;
      display:grid;
      gap:14px;
    }
    .sidebar-scroll, .editor-scroll {
      min-height:0;
      overflow:auto;
      padding-right:4px;
    }
    .status-grid {
      display:grid;
      gap:12px;
    }
    .status-item {
      padding:14px;
      border:1px solid var(--line);
      border-radius:16px;
      background:rgba(30,37,48,.86);
    }
    .status-item strong {
      display:block;
      margin-bottom:8px;
      color:var(--accent);
    }
    .status-item div {
      white-space:pre-wrap;
      line-height:1.5;
      color:#eef2fb;
    }
    .qr {
      width:100%;
      max-width:240px;
      aspect-ratio:1;
      object-fit:contain;
      border-radius:18px;
      border:1px solid var(--line);
      background:#fff;
      justify-self:center;
    }
    .form-grid {
      display:grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap:14px;
    }
    .field {
      display:grid;
      gap:8px;
    }
    .field label {
      color:var(--accent);
      font-size:14px;
      font-weight:600;
    }
    .field small {
      color:var(--muted);
      line-height:1.4;
    }
    .field.full { grid-column:1 / -1; }
    .toggle-grid {
      display:grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap:10px;
    }
    .check {
      display:flex;
      align-items:center;
      gap:10px;
      padding:12px 14px;
      border:1px solid var(--line);
      border-radius:14px;
      background:rgba(30,37,48,.7);
    }
    .check input { width:18px; height:18px; }
    .actions {
      display:grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap:10px;
    }
    .actions.sidebar-actions { grid-template-columns: 1fr 1fr; }
    .editor-nav {
      display:flex;
      flex-wrap:wrap;
      gap:10px;
    }
    .nav-button.active {
      background:linear-gradient(180deg, var(--accent), var(--accent-2));
      border-color:#f0b08a;
      color:#fffaf6;
      font-weight:600;
    }
    .editor-panel.hidden {
      display:none;
    }
    .quick-toolbar {
      display:flex;
      flex-wrap:wrap;
      gap:10px;
      align-items:center;
    }
    .quick-table-wrap {
      overflow:auto;
      border:1px solid var(--line);
      border-radius:16px;
      background:rgba(18,22,29,.68);
      padding:10px;
    }
    .quick-table {
      display:grid;
      gap:8px;
      min-width:840px;
    }
    .quick-row {
      display:grid;
      grid-template-columns: 34px minmax(180px, 1.5fr) 96px 132px 132px 120px 86px;
      gap:8px;
      align-items:center;
      padding:8px;
      border:1px solid transparent;
      border-radius:12px;
    }
    .quick-row.header {
      color:var(--muted);
      font-size:13px;
      padding-top:0;
      padding-bottom:4px;
    }
    .quick-row.data {
      background:rgba(30,37,48,.56);
      border-color:rgba(52,65,82,.5);
    }
    .quick-row.data.dirty {
      border-color:var(--accent);
      background:rgba(70,48,31,.34);
    }
    .quick-row.data.active {
      box-shadow: inset 0 0 0 1px rgba(240,155,102,.55);
    }
    .quick-row input[type="checkbox"] {
      width:18px;
      height:18px;
      margin:0 auto;
    }
    .quick-row .quick-name {
      font-weight:600;
      color:#eef2fb;
    }
    .quick-row select,
    .quick-row input[type="number"] {
      width:100%;
      padding:8px 10px;
    }
    .subsection-title {
      margin:2px 0 0;
      color:var(--accent);
      font-size:15px;
      font-weight:600;
    }
    .hint {
      padding:12px 14px;
      border:1px dashed var(--line);
      border-radius:14px;
      color:var(--muted);
      line-height:1.6;
      background:rgba(20,24,31,.6);
    }
    .pill {
      display:inline-flex;
      align-items:center;
      gap:8px;
      padding:8px 12px;
      border-radius:999px;
      border:1px solid var(--line);
      background:rgba(30,37,48,.9);
      color:var(--text);
      font-size:13px;
    }
    .good { color:var(--good); }
    .warn { color:var(--warn); }
    .danger { color:var(--danger); }
    #toast {
      position:fixed;
      right:20px;
      bottom:20px;
      padding:12px 16px;
      border-radius:14px;
      border:1px solid var(--line);
      background:rgba(23,28,36,.96);
      box-shadow:var(--shadow);
      color:var(--text);
      opacity:0;
      pointer-events:none;
      transform:translateY(8px);
      transition:opacity .18s ease, transform .18s ease;
      max-width:360px;
      z-index:10;
    }
    #toast.show {
      opacity:1;
      transform:translateY(0);
    }
    @media (max-width: 1100px) {
      .layout { grid-template-columns: 1fr; }
      .form-grid, .actions, .toggle-grid { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <main>
    <section class="topbar">
      <div class="title">
        <h1>DG-LAB 独立控制台</h1>
        <p>控制台现在完全脱离游戏内弹窗，所有操作都通过本地桥接服务完成。</p>
      </div>
      <div class="toolbar">
        <span class="pill" id="bindState">状态载入中</span>
        <button id="openPairPage">打开配对页</button>
        <button id="reloadConfig">重载配置</button>
        <button class="primary" id="saveSettings">保存全部设置</button>
      </div>
    </section>

    <section class="layout">
      <aside class="sidebar">
        <div class="panel sidebar-scroll">
          <div class="panel-header">
            <h2>运行概况</h2>
            <p>左侧侧边栏聚合运行状态、测试动作和预设切换。</p>
          </div>
          <div class="panel-body">
            <div class="status-grid">
              <div class="status-item"><strong>运行状态</strong><div id="statusText"></div></div>
              <div class="status-item"><strong>配对信息</strong><div id="pairText"></div></div>
              <div class="status-item"><strong>通道强度</strong><div id="strengthText"></div></div>
              <div class="status-item"><strong>最近事件与通知</strong><div id="noticeText"></div></div>
            </div>
            <img class="qr" id="pairQr" alt="配对二维码" />
            <div class="field full">
              <label for="presetPicker">当前预设</label>
              <select id="presetPicker"></select>
              <small id="presetDescription"></small>
            </div>
            <div class="field full">
              <label for="testWavePicker">测试波形</label>
              <select id="testWavePicker"></select>
            </div>
            <div class="form-grid">
              <div class="field">
                <label for="testStrengthA">A 通道测试值</label>
                <input id="testStrengthA" type="number" min="0" max="200" step="1" />
              </div>
              <div class="field">
                <label for="testStrengthB">B 通道测试值</label>
                <input id="testStrengthB" type="number" min="0" max="200" step="1" />
              </div>
            </div>
            <div class="actions sidebar-actions">
              <button id="saveStrengthA">保存 A 测试值</button>
              <button id="saveStrengthB">保存 B 测试值</button>
              <button id="testWaveA">测试波形 A</button>
              <button id="testWaveB">测试波形 B</button>
              <button id="testStrengthButtonA">发送强度 A</button>
              <button id="testStrengthButtonB">发送强度 B</button>
              <button id="toggleEnabled">启用 / 停用</button>
              <button id="clearBoth">清空双通道</button>
            </div>
            <div class="hint">
              控制台地址固定为当前本地桥接服务的 <code>/control</code>。热键与游戏内弹窗不再是主要入口。
            </div>
          </div>
        </div>
      </aside>

      <section class="editor">
        <div class="panel editor-scroll">
          <div class="panel-header">
            <h2>编辑区</h2>
            <p>默认打开全局设置。事件规则编辑保留在第二个面板里。</p>
          </div>
          <div class="panel-body">
            <div class="editor-nav">
              <button class="nav-button primary" id="showGlobalPanel">全局设置</button>
              <button class="nav-button" id="showRulePanel">事件规则</button>
            </div>

            <div class="editor-panel" id="globalSettingsPanel">
              <div class="form-grid">
                <div class="field">
                  <label for="publicHostOverride">配对地址覆盖</label>
                  <input id="publicHostOverride" type="text" placeholder="留空时自动选择局域网地址" />
                  <small>如果自动识别到的是 VPN / 虚拟网卡地址，在这里手动填手机可访问的局域网 IP。</small>
                </div>
                <div class="field">
                  <label for="globalCooldownSetting">全局冷却毫秒</label>
                  <input id="globalCooldownSetting" type="number" min="0" max="60000" step="50" />
                </div>
                <div class="field">
                  <label for="lowHealthThresholdSetting">低血量阈值百分比</label>
                  <input id="lowHealthThresholdSetting" type="number" min="1" max="100" step="1" />
                </div>
              </div>

              <div class="toggle-grid">
                <label class="check"><input id="globalEnabledSetting" type="checkbox" />全局启用</label>
                <label class="check"><input id="ignoreEventsWhileUnboundSetting" type="checkbox" />未绑定时忽略事件</label>
                <label class="check"><input id="autoClearOnCombatEndSetting" type="checkbox" />战斗结束自动清空</label>
                <label class="check"><input id="autoClearOnDisconnectSetting" type="checkbox" />断开连接自动清空</label>
              </div>

              <div class="actions">
                <button class="primary" id="saveGlobalSettings">保存全局设置</button>
                <button id="openPairPageGlobal">打开配对页</button>
              </div>
            </div>

            <div class="editor-panel hidden" id="ruleEditorPanel">
              <div class="hint">先在速改表里批量调整开关、模式、通道和冷却。需要细调某个事件时，再点右侧“精调”。</div>

              <div class="quick-toolbar">
                <button class="primary" id="saveQuickRules">保存速改表</button>
                <button id="copyRuleToChecked">把当前详细规则复制到勾选事件</button>
                <span class="pill" id="quickRuleSummary">0 项待保存 / 0 项勾选</span>
              </div>

              <div class="quick-table-wrap">
                <div class="quick-table" id="quickRuleTable"></div>
              </div>

              <div class="subsection-title">详细编辑</div>
              <div class="form-grid">
                <div class="field">
                  <label for="eventPicker">当前编辑事件</label>
                  <select id="eventPicker"></select>
                </div>
                <div class="field">
                  <label for="modePicker">触发模式</label>
                  <select id="modePicker">
                    <option value="Wave">波形</option>
                    <option value="Strength">强度</option>
                    <option value="Both" selected>强度 + 波形</option>
                  </select>
                </div>
                <div class="field">
                  <label for="channelUsagePicker">通道使用</label>
                  <select id="channelUsagePicker">
                    <option value="AOnly">仅 A 通道</option>
                    <option value="BOnly">仅 B 通道</option>
                    <option value="Both">双通道</option>
                  </select>
                </div>
                <div class="field">
                  <label for="strengthOperationPicker">强度操作</label>
                  <select id="strengthOperationPicker">
                    <option value="Set">设为绝对值</option>
                    <option value="DeltaUp">增加强度</option>
                    <option value="DeltaDown">降低强度</option>
                    <option value="Clear">清空通道</option>
                  </select>
                </div>
                <div class="field">
                  <label for="baseStrength">基础强度</label>
                  <input id="baseStrength" type="number" min="0" max="200" step="1" />
                </div>
                <div class="field">
                  <label for="maxStrength">最大强度</label>
                  <input id="maxStrength" type="number" min="0" max="200" step="1" />
                </div>
                <div class="field">
                  <label for="scalePerUnit">每单位增幅</label>
                  <input id="scalePerUnit" type="number" min="0" max="50" step="0.5" />
                </div>
                <div class="field">
                  <label for="cooldownMs">冷却毫秒</label>
                  <input id="cooldownMs" type="number" min="0" max="10000" step="50" />
                </div>
                <div class="field">
                  <label for="durationSeconds">持续秒数</label>
                  <input id="durationSeconds" type="number" min="1" max="30" step="1" />
                </div>
                <div class="field">
                  <label for="restoreAfterMs">恢复延迟毫秒</label>
                  <input id="restoreAfterMs" type="number" min="0" max="30000" step="50" />
                </div>
              </div>

              <div class="toggle-grid">
                <label class="check"><input id="enabledCheck" type="checkbox" />启用此事件</label>
                <label class="check"><input id="onlyInCombatCheck" type="checkbox" />仅战斗内触发</label>
                <label class="check"><input id="clearBeforeWaveCheck" type="checkbox" />波形前先清空通道</label>
                <label class="check"><input id="respectSoftLimitCheck" type="checkbox" />遵循 APP 软上限</label>
                <label class="check"><input id="randomizeWavesCheck" type="checkbox" />多波形随机选取</label>
              </div>

              <div class="field full">
                <label for="waveSelector">波形多选（按住 Ctrl / Shift 可多选）</label>
                <select id="waveSelector" multiple size="12"></select>
                <small>如果没有选中任何波形，保存时会自动回退到“连击”。</small>
              </div>

              <div class="actions">
                <button class="primary" id="saveRule">保存当前事件规则</button>
                <button id="refreshState">刷新状态</button>
                <button id="resetEditor">重置编辑器</button>
                <button id="openPairPageSecondary">打开配对页</button>
              </div>
            </div>
          </div>
        </div>
      </section>
    </section>
  </main>

  <div id="toast"></div>

  <script>
    const toastEl = document.getElementById('toast');
    let state = null;
    let editorMode = 'global';
    let selectedEvent = localStorage.getItem('dglab_selected_event') || 'PlayerHpLost';

    function showToast(message) {
      toastEl.textContent = message;
      toastEl.classList.add('show');
      clearTimeout(showToast._timer);
      showToast._timer = setTimeout(() => toastEl.classList.remove('show'), 2200);
    }

    async function api(path) {
      const response = await fetch(path, { cache: 'no-store' });
      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }
      const type = response.headers.get('content-type') || '';
      if (type.includes('application/json')) {
        return response.json();
      }
      return response.text();
    }

    function selectedValues(selectEl) {
      return Array.from(selectEl.selectedOptions).map(option => option.value);
    }

    function setSelectOptions(selectEl, options, selectedValue, valueField = 'value', textField = 'text') {
      const previous = selectedValue ?? selectEl.value;
      selectEl.innerHTML = '';
      for (const option of options) {
        const el = document.createElement('option');
        el.value = option[valueField];
        el.textContent = option[textField];
        selectEl.appendChild(el);
      }
      if (options.some(option => option[valueField] === previous)) {
        selectEl.value = previous;
      } else if (options.length > 0) {
        selectEl.value = options[0][valueField];
      }
    }

    function fillWaveSelector(selectedWaves) {
      const selector = document.getElementById('waveSelector');
      const selected = new Set(selectedWaves || []);
      selector.innerHTML = '';
      for (const wave of state.waves) {
        const option = document.createElement('option');
        option.value = wave;
        option.textContent = wave;
        option.selected = selected.has(wave);
        selector.appendChild(option);
      }
    }

    function currentRuleEntry() {
      return state.rules.find(rule => rule.eventType === selectedEvent) || null;
    }

    function setEditorMode(mode) {
      editorMode = mode === 'rule' ? 'rule' : 'global';
      document.getElementById('globalSettingsPanel').classList.toggle('hidden', editorMode !== 'global');
      document.getElementById('ruleEditorPanel').classList.toggle('hidden', editorMode !== 'rule');
      document.getElementById('showGlobalPanel').classList.toggle('primary', editorMode === 'global');
      document.getElementById('showGlobalPanel').classList.toggle('active', editorMode === 'global');
      document.getElementById('showRulePanel').classList.toggle('primary', editorMode === 'rule');
      document.getElementById('showRulePanel').classList.toggle('active', editorMode === 'rule');
    }

    function renderStatus() {
      const status = state.status;
      const enabledText = status.serverRunning ? '运行中' : '未启动';
      const frontendText = status.frontendConnected ? '已连接' : '未连接';
      const bindText = status.isBound ? '已绑定' : '未绑定';
      document.getElementById('bindState').textContent = `${status.currentPreset} / ${bindText}`;
      document.getElementById('bindState').className = `pill ${status.isBound ? 'good' : (status.frontendConnected ? 'warn' : 'danger')}`;

      document.getElementById('statusText').textContent =
        `全局启用：${status.globalEnabled ? '是' : '否'}\n服务端：${enabledText}\n前端：${frontendText}\n绑定：${bindText}\n连接数：${status.totalConnections} / 配对数：${status.totalPairs}`;
      document.getElementById('pairText').textContent =
        `监听地址：${status.publicHost}:${status.port}\n前端 ID：${status.frontendClientId || '-'}\nAPP ID：${status.targetId || '-'}\n控制台：${status.controlPanelUrl}`;
      document.getElementById('strengthText').textContent =
        `A 通道：${status.appStrengthA}/${status.appLimitA}\nB 通道：${status.appStrengthB}/${status.appLimitB}\n测试值 A：${status.configuredTestStrengthA}\n测试值 B：${status.configuredTestStrengthB}`;
      document.getElementById('noticeText').textContent =
        `最近事件：${status.lastEvent || '-'}\n通知：${status.lastNotice || '-'}\n反馈：${status.lastFeedback || '-'}\n错误：${status.lastError || '-'}`;

      const qr = document.getElementById('pairQr');
      qr.src = status.pairQrUrl || '';
      qr.style.display = status.pairQrUrl ? 'block' : 'none';

      document.getElementById('testStrengthA').value = status.configuredTestStrengthA;
      document.getElementById('testStrengthB').value = status.configuredTestStrengthB;
    }

    function renderPresetPicker() {
      const options = state.presets.map(preset => ({ value: preset.key, text: preset.displayName }));
      setSelectOptions(document.getElementById('presetPicker'), options, state.status.currentPreset);
      const current = state.presets.find(preset => preset.key === state.status.currentPreset);
      document.getElementById('presetDescription').textContent = current?.description || '';
    }

    function renderGlobalSettings() {
      const settings = state.globalSettings;
      document.getElementById('globalEnabledSetting').checked = !!settings.globalEnabled;
      document.getElementById('ignoreEventsWhileUnboundSetting').checked = !!settings.ignoreEventsWhileUnbound;
      document.getElementById('autoClearOnCombatEndSetting').checked = !!settings.autoClearOnCombatEnd;
      document.getElementById('autoClearOnDisconnectSetting').checked = !!settings.autoClearOnDisconnect;
      document.getElementById('globalCooldownSetting').value = settings.globalCooldownMs ?? 150;
      document.getElementById('lowHealthThresholdSetting').value = settings.lowHealthThresholdPercent ?? 30;
      document.getElementById('publicHostOverride').value = settings.publicHostOverride || '';
    }

    function renderEventPicker() {
      const options = state.events.map(eventItem => ({ value: eventItem.key, text: eventItem.displayName }));
      setSelectOptions(document.getElementById('eventPicker'), options, selectedEvent);
      selectedEvent = document.getElementById('eventPicker').value || selectedEvent;
      localStorage.setItem('dglab_selected_event', selectedEvent);
    }

    function quickRuleSnapshot(rule) {
      return JSON.stringify({
        enabled: !!rule.enabled,
        mode: rule.mode || 'Both',
        channelUsage: rule.channelUsage || 'AOnly',
        cooldownMs: Number(rule.cooldownMs ?? 0)
      });
    }

    function buildQuickModeSelect(value) {
      const select = document.createElement('select');
      for (const option of [
        { value: 'Wave', text: '波形' },
        { value: 'Strength', text: '强度' },
        { value: 'Both', text: '强度+波形' }
      ]) {
        const el = document.createElement('option');
        el.value = option.value;
        el.textContent = option.text;
        select.appendChild(el);
      }
      select.value = value || 'Both';
      return select;
    }

    function buildQuickChannelSelect(value) {
      const select = document.createElement('select');
      for (const option of [
        { value: 'AOnly', text: '仅 A' },
        { value: 'BOnly', text: '仅 B' },
        { value: 'Both', text: '双通道' }
      ]) {
        const el = document.createElement('option');
        el.value = option.value;
        el.textContent = option.text;
        select.appendChild(el);
      }
      select.value = value || 'AOnly';
      return select;
    }

    function collectQuickRowState(row) {
      return JSON.stringify({
        enabled: row.querySelector('.quick-enabled').checked,
        mode: row.querySelector('.quick-mode').value,
        channelUsage: row.querySelector('.quick-channel').value,
        cooldownMs: Number(row.querySelector('.quick-cooldown').value || 0)
      });
    }

    function updateQuickRuleSummary() {
      const rows = Array.from(document.querySelectorAll('.quick-row.data'));
      const dirtyCount = rows.filter(row => row.dataset.dirty === 'true').length;
      const checkedCount = rows.filter(row => row.querySelector('.quick-select').checked).length;
      document.getElementById('quickRuleSummary').textContent = `${dirtyCount} 项待保存 / ${checkedCount} 项勾选`;
    }

    function refreshQuickActiveRow() {
      for (const row of document.querySelectorAll('.quick-row.data')) {
        row.classList.toggle('active', row.dataset.eventType === selectedEvent);
      }
    }

    function refreshQuickRowDirtyState(row) {
      const dirty = collectQuickRowState(row) !== row.dataset.original;
      row.dataset.dirty = dirty ? 'true' : 'false';
      row.classList.toggle('dirty', dirty);
      row.classList.toggle('active', row.dataset.eventType === selectedEvent);
      updateQuickRuleSummary();
    }

    function openRuleFromQuickTable(eventType) {
      selectedEvent = eventType;
      localStorage.setItem('dglab_selected_event', selectedEvent);
      document.getElementById('eventPicker').value = selectedEvent;
      renderRuleEditor();
      refreshQuickActiveRow();
      setEditorMode('rule');
      showToast(`已切换到 ${state.events.find(item => item.key === eventType)?.displayName || eventType} 的详细编辑`);
    }

    function renderQuickRuleTable() {
      const table = document.getElementById('quickRuleTable');
      table.innerHTML = '';

      const header = document.createElement('div');
      header.className = 'quick-row header';
      for (const text of ['选', '事件', '启用', '触发模式', '通道', '冷却(ms)', '操作']) {
        const cell = document.createElement('div');
        cell.textContent = text;
        header.appendChild(cell);
      }
      table.appendChild(header);

      for (const eventInfo of state.events) {
        const entry = state.rules.find(rule => rule.eventType === eventInfo.key);
        if (!entry) {
          continue;
        }

        const row = document.createElement('div');
        row.className = 'quick-row data';
        row.dataset.eventType = eventInfo.key;
        row.dataset.original = quickRuleSnapshot(entry.rule);

        const selectBox = document.createElement('input');
        selectBox.type = 'checkbox';
        selectBox.className = 'quick-select';
        selectBox.addEventListener('change', updateQuickRuleSummary);

        const nameCell = document.createElement('div');
        nameCell.className = 'quick-name';
        nameCell.textContent = eventInfo.displayName;

        const enabledCell = document.createElement('input');
        enabledCell.type = 'checkbox';
        enabledCell.className = 'quick-enabled';
        enabledCell.checked = !!entry.rule.enabled;
        enabledCell.addEventListener('change', () => refreshQuickRowDirtyState(row));

        const modeCell = buildQuickModeSelect(entry.rule.mode);
        modeCell.classList.add('quick-mode');
        modeCell.addEventListener('change', () => refreshQuickRowDirtyState(row));

        const channelCell = buildQuickChannelSelect(entry.rule.channelUsage);
        channelCell.classList.add('quick-channel');
        channelCell.addEventListener('change', () => refreshQuickRowDirtyState(row));

        const cooldownCell = document.createElement('input');
        cooldownCell.type = 'number';
        cooldownCell.min = '0';
        cooldownCell.max = '10000';
        cooldownCell.step = '50';
        cooldownCell.className = 'quick-cooldown';
        cooldownCell.value = entry.rule.cooldownMs ?? 0;
        cooldownCell.addEventListener('input', () => refreshQuickRowDirtyState(row));

        const editButton = document.createElement('button');
        editButton.textContent = '精调';
        editButton.addEventListener('click', () => openRuleFromQuickTable(eventInfo.key));

        row.appendChild(selectBox);
        row.appendChild(nameCell);
        row.appendChild(enabledCell);
        row.appendChild(modeCell);
        row.appendChild(channelCell);
        row.appendChild(cooldownCell);
        row.appendChild(editButton);
        row.classList.toggle('active', eventInfo.key === selectedEvent);
        row.dataset.dirty = 'false';
        table.appendChild(row);
      }

      updateQuickRuleSummary();
    }

    function renderRuleEditor() {
      const entry = currentRuleEntry();
      if (!entry) {
        return;
      }

      const rule = entry.rule;
      document.getElementById('enabledCheck').checked = !!rule.enabled;
      document.getElementById('modePicker').value = rule.mode || 'Wave';
      document.getElementById('channelUsagePicker').value = rule.channelUsage || 'AOnly';
      document.getElementById('strengthOperationPicker').value = rule.strengthOperation || 'Set';
      document.getElementById('baseStrength').value = rule.baseStrength ?? 20;
      document.getElementById('maxStrength').value = rule.maxStrength ?? 80;
      document.getElementById('scalePerUnit').value = rule.scalePerUnit ?? 1;
      document.getElementById('cooldownMs').value = rule.cooldownMs ?? 500;
      document.getElementById('durationSeconds').value = rule.durationSeconds ?? 1;
      document.getElementById('restoreAfterMs').value = rule.restoreAfterMs ?? 0;
      document.getElementById('onlyInCombatCheck').checked = !!rule.onlyInCombat;
      document.getElementById('clearBeforeWaveCheck').checked = !!rule.clearChannelBeforeWave;
      document.getElementById('respectSoftLimitCheck').checked = !!rule.respectSoftLimit;
      document.getElementById('randomizeWavesCheck').checked = !!rule.randomizeWaves;
      fillWaveSelector(rule.waves?.length ? rule.waves : (rule.wave ? [rule.wave] : []));
    }

    function renderTestWavePicker() {
      const options = state.waves.map(wave => ({ value: wave, text: wave }));
      setSelectOptions(document.getElementById('testWavePicker'), options, document.getElementById('testWavePicker').value);
    }

    function buildRulePayload() {
      const waves = selectedValues(document.getElementById('waveSelector'));
      const fallbackWave = waves[0] || '连击';
      return {
        enabled: document.getElementById('enabledCheck').checked,
        mode: document.getElementById('modePicker').value,
        channelUsage: document.getElementById('channelUsagePicker').value,
        channel: document.getElementById('channelUsagePicker').value === 'BOnly' ? 'B' : 'A',
        cooldownMs: Number(document.getElementById('cooldownMs').value || 0),
        durationSeconds: Number(document.getElementById('durationSeconds').value || 1),
        wave: fallbackWave,
        waves,
        randomizeWaves: document.getElementById('randomizeWavesCheck').checked,
        baseStrength: Number(document.getElementById('baseStrength').value || 0),
        maxStrength: Number(document.getElementById('maxStrength').value || 0),
        scalePerUnit: Number(document.getElementById('scalePerUnit').value || 0),
        strengthOperation: document.getElementById('strengthOperationPicker').value,
        restoreAfterMs: Number(document.getElementById('restoreAfterMs').value || 0),
        clearChannelBeforeWave: document.getElementById('clearBeforeWaveCheck').checked,
        onlyInCombat: document.getElementById('onlyInCombatCheck').checked,
        respectSoftLimit: document.getElementById('respectSoftLimitCheck').checked
      };
    }

    function buildGlobalSettingsPayload() {
      return {
        globalEnabled: document.getElementById('globalEnabledSetting').checked,
        ignoreEventsWhileUnbound: document.getElementById('ignoreEventsWhileUnboundSetting').checked,
        autoClearOnCombatEnd: document.getElementById('autoClearOnCombatEndSetting').checked,
        autoClearOnDisconnect: document.getElementById('autoClearOnDisconnectSetting').checked,
        globalCooldownMs: Number(document.getElementById('globalCooldownSetting').value || 0),
        lowHealthThresholdPercent: Number(document.getElementById('lowHealthThresholdSetting').value || 30),
        publicHostOverride: document.getElementById('publicHostOverride').value.trim()
      };
    }

    async function refreshState() {
      state = await api('/api/control/state');
      setEditorMode(editorMode);
      renderStatus();
      renderGlobalSettings();
      renderPresetPicker();
      renderEventPicker();
      renderQuickRuleTable();
      renderRuleEditor();
      renderTestWavePicker();
    }

    async function callAndRefresh(path, message, skipRefresh = false) {
      await api(path);
      if (!skipRefresh) {
        await refreshState();
      }
      if (message) {
        showToast(message);
      }
    }

    async function saveQuickRules() {
      const dirtyRows = Array.from(document.querySelectorAll('.quick-row.data')).filter(row => row.dataset.dirty === 'true');
      if (dirtyRows.length === 0) {
        showToast('速改表没有待保存的项目');
        return;
      }

      for (const row of dirtyRows) {
        const eventType = row.dataset.eventType;
        const entry = state.rules.find(rule => rule.eventType === eventType);
        if (!entry) {
          continue;
        }

        const rule = JSON.parse(JSON.stringify(entry.rule));
        rule.enabled = row.querySelector('.quick-enabled').checked;
        rule.mode = row.querySelector('.quick-mode').value;
        rule.channelUsage = row.querySelector('.quick-channel').value;
        rule.channel = rule.channelUsage === 'BOnly' ? 'B' : 'A';
        rule.cooldownMs = Number(row.querySelector('.quick-cooldown').value || 0);

        await api(`/api/control/rule/save?eventType=${encodeURIComponent(eventType)}&rule=${encodeURIComponent(JSON.stringify(rule))}`);
      }

      await refreshState();
      showToast(`已保存 ${dirtyRows.length} 条速改规则`);
    }

    async function copyCurrentRuleToCheckedEvents() {
      const targetRows = Array.from(document.querySelectorAll('.quick-row.data')).filter(row => row.querySelector('.quick-select').checked);
      if (targetRows.length === 0) {
        showToast('先勾选要同步的事件');
        return;
      }

      const rule = buildRulePayload();
      for (const row of targetRows) {
        const eventType = row.dataset.eventType;
        await api(`/api/control/rule/save?eventType=${encodeURIComponent(eventType)}&rule=${encodeURIComponent(JSON.stringify(rule))}`);
      }

      await refreshState();
      showToast(`当前详细规则已复制到 ${targetRows.length} 个事件`);
    }

    function bindEvents() {
      document.getElementById('showGlobalPanel').addEventListener('click', () => setEditorMode('global'));
      document.getElementById('showRulePanel').addEventListener('click', () => setEditorMode('rule'));

      document.getElementById('presetPicker').addEventListener('change', async event => {
        const key = event.target.value;
        await callAndRefresh(`/api/control/preset?name=${encodeURIComponent(key)}`, `预设已切换到 ${key}`);
      });

      document.getElementById('eventPicker').addEventListener('change', event => {
        selectedEvent = event.target.value;
        localStorage.setItem('dglab_selected_event', selectedEvent);
        renderRuleEditor();
        refreshQuickActiveRow();
      });

      document.getElementById('saveRule').addEventListener('click', async () => {
        const rule = buildRulePayload();
        await callAndRefresh(
          `/api/control/rule/save?eventType=${encodeURIComponent(selectedEvent)}&rule=${encodeURIComponent(JSON.stringify(rule))}`,
          '当前事件规则已保存');
      });

      document.getElementById('saveQuickRules').addEventListener('click', saveQuickRules);
      document.getElementById('copyRuleToChecked').addEventListener('click', copyCurrentRuleToCheckedEvents);

      document.getElementById('saveGlobalSettings').addEventListener('click', async () => {
        const settings = buildGlobalSettingsPayload();
        await callAndRefresh(
          `/api/control/global/save?settings=${encodeURIComponent(JSON.stringify(settings))}`,
          '全局设置已保存');
      });

      document.getElementById('refreshState').addEventListener('click', async () => {
        await refreshState();
        showToast('状态已刷新');
      });

      document.getElementById('resetEditor').addEventListener('click', () => {
        renderRuleEditor();
        showToast('编辑器已重置到当前已保存规则');
      });

      document.getElementById('toggleEnabled').addEventListener('click', async () => {
        await callAndRefresh('/api/control/toggle-enabled', '全局启用状态已切换');
      });

      document.getElementById('reloadConfig').addEventListener('click', async () => {
        await callAndRefresh('/api/control/reload', '配置与波形库已重载');
      });

      document.getElementById('saveSettings').addEventListener('click', async () => {
        await callAndRefresh('/api/control/save-settings', '设置已保存');
      });

      document.getElementById('clearBoth').addEventListener('click', async () => {
        await callAndRefresh('/api/control/clear', '已下发双通道清空');
      });

      document.getElementById('saveStrengthA').addEventListener('click', async () => {
        const value = document.getElementById('testStrengthA').value;
        await callAndRefresh(`/api/control/configure-strength?channel=A&value=${encodeURIComponent(value)}`, 'A 通道测试值已保存');
      });

      document.getElementById('saveStrengthB').addEventListener('click', async () => {
        const value = document.getElementById('testStrengthB').value;
        await callAndRefresh(`/api/control/configure-strength?channel=B&value=${encodeURIComponent(value)}`, 'B 通道测试值已保存');
      });

      document.getElementById('testWaveA').addEventListener('click', async () => {
        const wave = document.getElementById('testWavePicker').value;
        await callAndRefresh(`/api/control/test-wave?channel=A&wave=${encodeURIComponent(wave)}`, '已发送测试波形到 A', true);
      });

      document.getElementById('testWaveB').addEventListener('click', async () => {
        const wave = document.getElementById('testWavePicker').value;
        await callAndRefresh(`/api/control/test-wave?channel=B&wave=${encodeURIComponent(wave)}`, '已发送测试波形到 B', true);
      });

      document.getElementById('testStrengthButtonA').addEventListener('click', async () => {
        const value = document.getElementById('testStrengthA').value;
        await callAndRefresh(`/api/control/test-strength?channel=A&value=${encodeURIComponent(value)}`, '已发送 A 通道强度', true);
      });

      document.getElementById('testStrengthButtonB').addEventListener('click', async () => {
        const value = document.getElementById('testStrengthB').value;
        await callAndRefresh(`/api/control/test-strength?channel=B&value=${encodeURIComponent(value)}`, '已发送 B 通道强度', true);
      });

      const openPair = () => window.open('/pair', '_blank', 'noopener');
      document.getElementById('openPairPage').addEventListener('click', openPair);
      document.getElementById('openPairPageGlobal').addEventListener('click', openPair);
      document.getElementById('openPairPageSecondary').addEventListener('click', openPair);
    }

    bindEvents();
    refreshState().catch(error => {
      console.error(error);
      showToast(`控制台加载失败：${error.message}`);
    });
  </script>
</body>
</html>
""";

        return html;
    }

    public void OnCombatStarted(RoomType roomType)
    {
        lock (_gate)
        {
            _combatActive = true;
            EmitEventUnsafe(new BridgeEvent
            {
                Type = roomType switch
                {
                    RoomType.Elite => BridgeEventType.EliteCombatStart,
                    RoomType.Boss => BridgeEventType.BossCombatStart,
                    _ => BridgeEventType.CombatStart
                },
                Magnitude = 1,
                Description = $"Combat started ({roomType})"
            });
        }
    }

    public void OnCombatVictory()
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.CombatVictory,
                Magnitude = 1,
                Description = "Combat victory"
            });
        }
    }

    public void OnCombatEnded()
    {
        lock (_gate)
        {
            _combatActive = false;
            if (_config.Safety.AutoClearOnCombatEnd)
            {
                _ = ClearAllAsync();
            }
        }
    }

    public void OnPlayerDamageTaken(int amount, string source)
    {
        if (amount <= 0)
        {
            return;
        }

        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.PlayerHpLost,
                Magnitude = amount,
                Description = $"Damage taken: {amount} ({source})"
            });
        }
    }

    public void OnPlayerHealed(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.PlayerHealed,
                Magnitude = amount,
                Description = $"Healed: {amount}"
            });
        }
    }

    public void OnPlayerHpStateChanged(int currentHp, int maxHp)
    {
        lock (_gate)
        {
            var threshold = Math.Max(1, _config.Safety.LowHealthThresholdPercent);
            var percent = maxHp <= 0 ? 0.0 : currentHp * 100.0 / maxHp;
            if (!_lowHealthActive && percent <= threshold)
            {
                _lowHealthActive = true;
                EmitEventUnsafe(new BridgeEvent
                {
                    Type = BridgeEventType.LowHealthEntered,
                    Magnitude = threshold - percent,
                    Description = $"Low health entered ({currentHp}/{maxHp})"
                });
            }
            else if (_lowHealthActive && percent > threshold)
            {
                _lowHealthActive = false;
                EmitEventUnsafe(new BridgeEvent
                {
                    Type = BridgeEventType.LowHealthExited,
                    Magnitude = percent,
                    Description = $"Low health exited ({currentHp}/{maxHp})"
                });
            }
        }
    }

    public void OnPlayerDeath()
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.PlayerDeath,
                Magnitude = 1,
                Description = "Player death"
            });
        }
    }

    public void OnCardPlayed(string cardId)
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.CardPlayed,
                Magnitude = 1,
                Description = $"Card played: {cardId}"
            });
        }
    }

    public void OnGoldGained(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.GoldGained,
                Magnitude = amount,
                Description = $"Gold gained: {amount}"
            });
        }
    }

    public void OnPotionUsed(string potionId)
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.PotionUsed,
                Magnitude = 1,
                Description = $"Potion used: {potionId}"
            });
        }
    }

    public void OnPotionObtained(string potionId)
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.PotionObtained,
                Magnitude = 1,
                Description = $"Potion obtained: {potionId}"
            });
        }
    }

    public void OnRewardTaken(BridgeEventType type, string description)
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = type,
                Magnitude = 1,
                Description = description
            });
        }
    }

    public void OnMerchantPurchase(string description, int goldSpent)
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.MerchantPurchase,
                Magnitude = goldSpent,
                Description = description
            });
        }
    }

    public void OnBlockBroken()
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.BlockBroken,
                Magnitude = 1,
                Description = "Player block broken"
            });
        }
    }

    public void OnTurnStart()
    {
        lock (_gate)
        {
            EmitEventUnsafe(new BridgeEvent
            {
                Type = BridgeEventType.TurnStart,
                Magnitude = 1,
                Description = "Turn start"
            });
        }
    }

    private void EmitEventUnsafe(BridgeEvent bridgeEvent)
    {
        var nowTicks = System.Environment.TickCount64;
        _lastEvent = bridgeEvent.Description;

        if (!_config.Safety.GlobalEnabled)
        {
            return;
        }

        if (_config.Safety.GlobalCooldownMs > 0 && nowTicks - _globalLastEventTicks < _config.Safety.GlobalCooldownMs)
        {
            return;
        }

        if (!_config.Presets.TryGetValue(_config.CurrentPreset, out var preset))
        {
            return;
        }

        if (!preset.Rules.TryGetValue(bridgeEvent.Type, out var rule) || !rule.Enabled)
        {
            return;
        }

        if (rule.OnlyInCombat && !_combatActive)
        {
            return;
        }

        if (_config.Safety.IgnoreEventsWhileUnbound && !(_frontend?.IsBound ?? false))
        {
            return;
        }

        if (_eventCooldowns.TryGetValue(bridgeEvent.Type, out var lastTicks)
            && nowTicks - lastTicks < rule.CooldownMs)
        {
            return;
        }

        _eventCooldowns[bridgeEvent.Type] = nowTicks;
        _globalLastEventTicks = nowTicks;

        _ = Task.Run(() => DispatchRuleAsync(rule, bridgeEvent));
    }

    private async Task DispatchRuleAsync(EventRuleConfig rule, BridgeEvent bridgeEvent)
    {
        try
        {
            if (_frontend == null)
            {
                return;
            }

            var channels = ResolveChannels(rule).ToArray();
            if (channels.Length == 0)
            {
                return;
            }

            if (rule.ClearChannelBeforeWave)
            {
                foreach (var channel in channels)
                {
                    await _frontend.ClearChannelAsync(channel);
                }
                await Task.Delay(120);
            }

            if (rule.Mode is TriggerMode.Strength or TriggerMode.Both)
            {
                var rawStrength = Math.Clamp(
                    rule.StrengthOperation == StrengthOperation.Clear ? 0 : rule.BaseStrength + (int)Math.Round(bridgeEvent.Magnitude * rule.ScalePerUnit),
                    0,
                    Math.Max(rule.MaxStrength, rule.BaseStrength));

                foreach (var channel in channels)
                {
                    var clampedStrength = ClampStrengthToSoftLimit(channel, rawStrength, rule);
                    var previousValue = channel == ChannelRef.A ? _frontend.StrengthA : _frontend.StrengthB;
                    await _frontend.SendStrengthAsync(channel, rule.StrengthOperation, clampedStrength);
                    if (rule.RestoreAfterMs > 0 && rule.StrengthOperation != StrengthOperation.Clear)
                    {
                        ScheduleStrengthRestore(channel, previousValue, rule.RestoreAfterMs);
                    }
                }
            }

            if (rule.Mode is TriggerMode.Wave or TriggerMode.Both)
            {
                var selectedWave = SelectWaveName(rule);
                foreach (var channel in channels)
                {
                    await _frontend.SendWaveAsync(channel, WaveLibrary.GetFrames(selectedWave), rule.DurationSeconds);
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to dispatch rule for {bridgeEvent.Type}: {ex.Message}");
        }
    }

    private void ScheduleStrengthRestore(ChannelRef channel, int restoreValue, int delayMs)
    {
        lock (_gate)
        {
            _strengthGeneration[channel] = _strengthGeneration.TryGetValue(channel, out var current) ? current + 1 : 1;
            var generation = _strengthGeneration[channel];
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs);
                    lock (_gate)
                    {
                        if (!_strengthGeneration.TryGetValue(channel, out var activeGeneration) || activeGeneration != generation)
                        {
                            return;
                        }
                    }

                    await (_frontend?.SendStrengthAsync(channel, StrengthOperation.Set, restoreValue) ?? Task.CompletedTask);
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"Failed to restore channel strength: {ex.Message}");
                }
            });
        }
    }

    private void NormalizeRule(EventRuleConfig rule)
    {
        if (rule.Waves.Count == 0 && !string.IsNullOrWhiteSpace(rule.Wave))
        {
            rule.Waves = new List<string> { rule.Wave };
        }

        if (rule.Waves.Count > 0)
        {
            rule.Wave = rule.Waves[0];
        }

        if (rule.ChannelUsage == ChannelUsageMode.AOnly)
        {
            rule.Channel = ChannelRef.A;
        }
        else if (rule.ChannelUsage == ChannelUsageMode.BOnly)
        {
            rule.Channel = ChannelRef.B;
        }
    }

    private static EventRuleConfig CloneRule(EventRuleConfig source)
    {
        return new EventRuleConfig
        {
            Enabled = source.Enabled,
            Mode = source.Mode,
            Channel = source.Channel,
            ChannelUsage = source.ChannelUsage,
            CooldownMs = source.CooldownMs,
            DurationSeconds = source.DurationSeconds,
            Wave = source.Wave,
            Waves = source.Waves.ToList(),
            RandomizeWaves = source.RandomizeWaves,
            BaseStrength = source.BaseStrength,
            MaxStrength = source.MaxStrength,
            ScalePerUnit = source.ScalePerUnit,
            StrengthOperation = source.StrengthOperation,
            RestoreAfterMs = source.RestoreAfterMs,
            ClearChannelBeforeWave = source.ClearChannelBeforeWave,
            OnlyInCombat = source.OnlyInCombat,
            RespectSoftLimit = source.RespectSoftLimit
        };
    }

    private IEnumerable<ChannelRef> ResolveChannels(EventRuleConfig rule)
    {
        return rule.ChannelUsage switch
        {
            ChannelUsageMode.BOnly => new[] { ChannelRef.B },
            ChannelUsageMode.Both => new[] { ChannelRef.A, ChannelRef.B },
            _ => new[] { ChannelRef.A }
        };
    }

    private int ClampStrengthToSoftLimit(ChannelRef channel, int value, EventRuleConfig rule)
    {
        if (!rule.RespectSoftLimit || _frontend == null)
        {
            return Math.Clamp(value, 0, 200);
        }

        var softLimit = channel == ChannelRef.A ? _frontend.LimitA : _frontend.LimitB;
        if (softLimit <= 0)
        {
            return Math.Clamp(value, 0, 200);
        }

        return Math.Clamp(value, 0, softLimit);
    }

    private string SelectWaveName(EventRuleConfig rule)
    {
        var waves = rule.Waves.Count > 0 ? rule.Waves : new List<string> { rule.Wave };
        if (waves.Count == 0)
        {
            return "连击";
        }

        if (!rule.RandomizeWaves || waves.Count == 1)
        {
            return waves[0];
        }

        var index = Random.Shared.Next(0, waves.Count);
        return waves[index];
    }

    private static string GetPresetDisplayName(string presetName, PresetConfig? preset = null)
    {
        return presetName switch
        {
            "Balanced" => "平衡",
            "CombatHeavy" => "战斗强化",
            "RewardHeavy" => "奖励强化",
            "Minimal" => "极简",
            _ when !string.IsNullOrWhiteSpace(preset?.DisplayName) => preset!.DisplayName,
            _ => presetName
        };
    }

    private static string FormatHotkey(HotkeyConfig config)
    {
        var parts = new List<string>(4);
        if (config.Ctrl)
        {
            parts.Add("Ctrl");
        }
        if (config.Alt)
        {
            parts.Add("Alt");
        }
        if (config.Shift)
        {
            parts.Add("Shift");
        }
        parts.Add(string.IsNullOrWhiteSpace(config.Key) ? "F7" : config.Key.ToUpperInvariant());
        return string.Join("+", parts);
    }

    private static bool TryGetStableKeyName(InputEventKey keyEvent, out string keyName)
    {
        var key = keyEvent.PhysicalKeycode != Key.None ? keyEvent.PhysicalKeycode : keyEvent.Keycode;
        if (key == Key.None
            || key is Key.Ctrl or Key.Alt or Key.Shift
            || key is Key.Unknown)
        {
            keyName = string.Empty;
            return false;
        }

        keyName = key.ToString();
        return true;
    }

    private static string GetEventDisplayName(BridgeEventType eventType)
    {
        return eventType switch
        {
            BridgeEventType.PlayerHpLost => "玩家掉血",
            BridgeEventType.PlayerHealed => "玩家治疗",
            BridgeEventType.LowHealthEntered => "进入濒死",
            BridgeEventType.LowHealthExited => "脱离濒死",
            BridgeEventType.CombatStart => "普通战开始",
            BridgeEventType.EliteCombatStart => "精英战开始",
            BridgeEventType.BossCombatStart => "Boss 战开始",
            BridgeEventType.CombatVictory => "战斗胜利",
            BridgeEventType.PlayerDeath => "玩家死亡",
            BridgeEventType.CardPlayed => "打出卡牌",
            BridgeEventType.GoldGained => "获得金币",
            BridgeEventType.PotionUsed => "使用药水",
            BridgeEventType.PotionObtained => "获得药水",
            BridgeEventType.RewardCardTaken => "领取卡牌奖励",
            BridgeEventType.RewardRelicTaken => "领取遗物奖励",
            BridgeEventType.RewardGoldTaken => "领取金币奖励",
            BridgeEventType.RewardPotionTaken => "领取药水奖励",
            BridgeEventType.MerchantPurchase => "商店购买",
            BridgeEventType.BlockBroken => "玩家破防",
            BridgeEventType.TurnStart => "回合开始",
            _ => eventType.ToString()
        };
    }
}
