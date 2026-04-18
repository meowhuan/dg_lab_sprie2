using DgLabSocketSpire2.Bridge;
using DgLabSocketSpire2.Configuration;
using Godot;

namespace DgLabSocketSpire2.UI;

internal sealed partial class BridgeHotkeyNode : Node
{
    private bool _activated;

    public override void _Ready()
    {
        Activate();
        UiLog.Info("Hotkey node ready.");
    }

    public void Activate()
    {
        if (_activated)
        {
            return;
        }

        Name = "DgLabHotkeyNode";
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        SetProcessInput(true);
        SetProcessUnhandledInput(true);
        SetProcessShortcutInput(true);
        _activated = true;
        UiLog.Info("Hotkey node activated.");
    }

    public override void _Input(InputEvent @event)
    {
        HandleKeyInput(@event, "_Input");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        HandleKeyInput(@event, "_UnhandledInput");
    }

    public override void _ShortcutInput(InputEvent @event)
    {
        HandleKeyInput(@event, "_ShortcutInput");
    }

    private void HandleKeyInput(InputEvent @event, string source)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        var service = BridgeService.Instance;
        if (service.IsHotkeyCaptureActive() || service.IsHotkeyTestActive())
        {
            UiLog.Info($"{source} received key: {DescribeKeyEvent(keyEvent)} capture={service.IsHotkeyCaptureActive()} test={service.IsHotkeyTestActive()}");
        }

        if (service.IsHotkeyCaptureActive())
        {
            var consumed = service.CompleteHotkeyCapture(keyEvent);
            UiLog.Info($"{source} hotkey capture attempt: consumed={consumed}, key={DescribeKeyEvent(keyEvent)}, now={service.GetHotkeyDisplayText()}");
            if (consumed)
            {
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (!MatchesHotkey(service.GetHotkeyConfig(), keyEvent))
        {
            return;
        }

        if (service.TryConsumeHotkeyTestPress())
        {
            UiLog.Info($"{source} hotkey test succeeded with {service.GetHotkeyDisplayText()}");
        }
        else
        {
            UiLog.Info($"{source} hotkey matched and toggled dialog with {service.GetHotkeyDisplayText()}");
            BridgeUiHost.Instance.ToggleDialog();
        }

        GetViewport().SetInputAsHandled();
    }

    private static bool MatchesHotkey(HotkeyConfig config, InputEventKey keyEvent)
    {
        if (!TryParseKey(config.Key, out var key))
        {
            return false;
        }

        var physical = keyEvent.PhysicalKeycode;
        var logical = keyEvent.Keycode;
        var keyMatched = logical == key || (physical != Key.None && physical == key);
        if (!keyMatched)
        {
            return false;
        }

        return config.Ctrl == keyEvent.CtrlPressed
            && config.Alt == keyEvent.AltPressed
            && config.Shift == keyEvent.ShiftPressed;
    }

    private static bool TryParseKey(string keyText, out Key key)
    {
        if (Enum.TryParse<Key>(keyText, true, out key))
        {
            return key != Key.None && key != Key.Unknown;
        }

        key = Key.None;
        return false;
    }

    private static string DescribeKeyEvent(InputEventKey keyEvent)
    {
        return $"key={keyEvent.Keycode}, physical={keyEvent.PhysicalKeycode}, ctrl={keyEvent.CtrlPressed}, alt={keyEvent.AltPressed}, shift={keyEvent.ShiftPressed}";
    }
}
