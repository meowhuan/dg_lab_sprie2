using DgLabSocketSpire2.Bridge;
using DgLabSocketSpire2.Configuration;
using Godot;

namespace DgLabSocketSpire2.UI;

public sealed partial class DgLabOverlay : CanvasLayer
{
    private readonly BridgeService _service;
    private Button? _toggleButton;
    private PanelContainer? _panel;
    private Label? _statusLabel;
    private Label? _strengthLabel;
    private Label? _pairLabel;
    private Label? _noticeLabel;
    private OptionButton? _presetPicker;
    private bool _wasF7Down;
    private bool _wasF8Down;

    public DgLabOverlay(BridgeService service)
    {
        _service = service;
        ProcessMode = ProcessModeEnum.Always;
        Layer = 1000;
    }

    public override void _Ready()
    {
        Name = "DgLabOverlay";
        Visible = true;

        _toggleButton = new Button
        {
            Name = "ToggleButton",
            Text = "Hide DG-LAB",
            Position = new Vector2(20, 20),
            Size = new Vector2(140, 40),
            FocusMode = Control.FocusModeEnum.None
        };
        _toggleButton.TopLevel = true;
        _toggleButton.ZIndex = 1000;
        _toggleButton.Pressed += TogglePanel;
        AddChild(_toggleButton);

        _panel = new PanelContainer
        {
            Name = "Panel",
            Visible = true,
            Position = new Vector2(20, 70),
            Size = new Vector2(500, 360),
            FocusMode = Control.FocusModeEnum.None
        };
        _panel.TopLevel = true;
        _panel.ZIndex = 999;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var title = new Label
        {
            Text = "DG-LAB SOCKET CONTROL",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        root.AddChild(title);

        _statusLabel = new Label();
        _pairLabel = new Label();
        _strengthLabel = new Label();
        _noticeLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_statusLabel);
        root.AddChild(_pairLabel);
        root.AddChild(_strengthLabel);
        root.AddChild(_noticeLabel);

        _presetPicker = new OptionButton();
        foreach (var preset in _service.GetPresetNames())
        {
            _presetPicker.AddItem(preset);
        }
        SelectPreset(_service.GetCurrentPreset());
        _presetPicker.ItemSelected += index =>
        {
            var text = _presetPicker.GetItemText((int)index);
            _service.SetPreset(text);
        };
        root.AddChild(_presetPicker);

        var buttons = new GridContainer
        {
            Columns = 2
        };
        buttons.AddThemeConstantOverride("h_separation", 8);
        buttons.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(buttons);

        buttons.AddChild(MakeButton("Open Pair Page", () => _service.OpenPairPage()));
        buttons.AddChild(MakeButton("Toggle Enabled", () => _service.ToggleEnabled()));
        buttons.AddChild(MakeButton("Test Wave A", async () => await _service.SendTestPulseAsync("连击", ChannelRef.A)));
        buttons.AddChild(MakeButton("Test Wave B", async () => await _service.SendTestPulseAsync("潮汐", ChannelRef.B)));
        buttons.AddChild(MakeButton("Set A=35", async () => await _service.SendTestStrengthAsync(ChannelRef.A, 35)));
        buttons.AddChild(MakeButton("Set B=35", async () => await _service.SendTestStrengthAsync(ChannelRef.B, 35)));
        buttons.AddChild(MakeButton("Clear Both", async () => await _service.ClearAllAsync()));
        buttons.AddChild(MakeButton("Reload Config", () => _service.ReloadConfig()));

        AddChild(_panel);
        UpdateToggleCaption();
        ModLog.Info("Overlay UI ready.");
    }

    public override void _Process(double delta)
    {
        if (_panel == null || _statusLabel == null || _pairLabel == null || _strengthLabel == null || _noticeLabel == null)
        {
            return;
        }

        var status = _service.GetStatusSnapshot();
        SelectPreset(status.CurrentPreset);
        _statusLabel.Text = $"F7 打开/关闭面板 | Enabled={_service.IsEnabled()} | Server={status.ServerRunning} | Frontend={status.FrontendConnected} | Bound={status.IsBound}";
        _pairLabel.Text = $"Host={status.PublicHost}:{status.Port}\nClient={status.FrontendClientId ?? "-"}\nTarget={status.TargetId ?? "-"}";
        _strengthLabel.Text = $"A={status.AppStrengthA}/{status.AppLimitA}  B={status.AppStrengthB}/{status.AppLimitB}";
        _noticeLabel.Text = $"Last: {status.LastEvent}\nNotice: {status.LastNotice}\nFeedback: {status.LastFeedback}\nError: {status.LastError}";

        var f7Down = Input.IsKeyPressed(Key.F7);
        var f8Down = Input.IsKeyPressed(Key.F8);
        if (f7Down && !_wasF7Down)
        {
            TogglePanel();
        }
        else if (f8Down && !_wasF8Down)
        {
            TogglePanel();
        }

        _wasF7Down = f7Down;
        _wasF8Down = f8Down;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent
            && keyEvent.Pressed
            && !keyEvent.Echo
            && (keyEvent.Keycode == Key.F7
                || keyEvent.Keycode == Key.F8
                || keyEvent.PhysicalKeycode == Key.F7
                || keyEvent.PhysicalKeycode == Key.F8))
        {
            TogglePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void TogglePanel()
    {
        if (_panel == null)
        {
            return;
        }

        _panel.Visible = !_panel.Visible;
        UpdateToggleCaption();
        ModLog.Info($"Overlay panel toggled: visible={_panel.Visible}");
    }

    private void UpdateToggleCaption()
    {
        if (_toggleButton == null || _panel == null)
        {
            return;
        }

        _toggleButton.Text = _panel.Visible ? "Hide DG-LAB" : "DG-LAB";
    }

    private void SelectPreset(string presetName)
    {
        if (_presetPicker == null)
        {
            return;
        }

        for (var index = 0; index < _presetPicker.ItemCount; index++)
        {
            if (string.Equals(_presetPicker.GetItemText(index), presetName, StringComparison.OrdinalIgnoreCase))
            {
                _presetPicker.Select(index);
                return;
            }
        }
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        button.Pressed += action;
        return button;
    }

    private static Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        button.Pressed += () => _ = action();
        return button;
    }
}
