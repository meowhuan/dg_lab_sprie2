using DgLabSocketSpire2.Bridge;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace DgLabSocketSpire2.UI;

internal sealed class BridgeUiHost
{
    private Button? _mainMenuButton;
    private Button? _moddingButton;
    private bool _announcedExternalMode;

    public static BridgeUiHost Instance { get; } = new();

    private BridgeUiHost()
    {
    }

    public void EnsureMainMenuButton(NMainMenu menu, BridgeService service)
    {
        try
        {
            if (_mainMenuButton != null && IsAlive(_mainMenuButton))
            {
                return;
            }

            var button = new Button
            {
                Name = "DgLabMainMenuButton",
                Text = "DG-LAB 控制台",
                TopLevel = true,
                Position = new Vector2(24, 110),
                Size = new Vector2(170, 40),
                FocusMode = Control.FocusModeEnum.None,
                Visible = true
            };
            StyleButton(button);
            button.Pressed += service.OpenControlPanel;
            menu.AddChild(button);
            _mainMenuButton = button;
            UiLog.Info($"External control button added to main menu. pos={button.Position}, size={button.Size}");
        }
        catch (Exception ex)
        {
            UiLog.Error("Failed to add main menu control button.", ex);
        }
    }

    public void EnsureModdingButton(NModdingScreen screen, BridgeService service)
    {
        try
        {
            if (_moddingButton != null && IsAlive(_moddingButton))
            {
                return;
            }

            var button = new Button
            {
                Name = "DgLabModdingButton",
                Text = "打开 DG-LAB 控制台",
                TopLevel = true,
                Position = new Vector2(36, 36),
                Size = new Vector2(280, 42),
                FocusMode = Control.FocusModeEnum.None,
                Visible = true
            };
            StyleButton(button);
            button.Pressed += service.OpenControlPanel;
            screen.AddChild(button);
            _moddingButton = button;
            UiLog.Info($"External control button added to modding screen. pos={button.Position}, size={button.Size}");
        }
        catch (Exception ex)
        {
            UiLog.Error("Failed to add modding control button.", ex);
        }
    }

    public void EnsureDialog(BridgeService service)
    {
        if (_announcedExternalMode)
        {
            return;
        }

        _announcedExternalMode = true;
        UiLog.Info("In-game dialog disabled. External control panel is served at /control.");
    }

    public void ToggleDialog()
    {
        BridgeService.Instance.OpenControlPanel();
    }

    private static bool IsAlive(GodotObject? obj)
    {
        return obj != null && GodotObject.IsInstanceValid(obj);
    }

    private static void StyleButton(Button button)
    {
        button.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.10f, 1.0f));
        button.AddThemeColorOverride("font_hover_color", new Color(0.16f, 0.14f, 0.10f, 1.0f));
        button.AddThemeColorOverride("font_pressed_color", new Color(0.16f, 0.14f, 0.10f, 1.0f));
    }
}
