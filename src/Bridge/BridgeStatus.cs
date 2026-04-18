namespace DgLabSocketSpire2.Bridge;

public sealed class BridgeStatus
{
    public bool GlobalEnabled { get; init; }

    public bool ServerRunning { get; init; }

    public bool FrontendConnected { get; init; }

    public bool IsBound { get; init; }

    public string PublicHost { get; init; } = "127.0.0.1";

    public int Port { get; init; }

    public string PairPageUrl { get; init; } = string.Empty;

    public string ControlPanelUrl { get; init; } = string.Empty;

    public string? FrontendClientId { get; init; }

    public string? TargetId { get; init; }

    public string? PairSocketUrl { get; init; }

    public string? PairQrUrl { get; init; }

    public string CurrentPreset { get; init; } = "Balanced";

    public string CurrentPresetDescription { get; init; } = string.Empty;

    public string CurrentHotkey { get; init; } = "F7";

    public bool HotkeyCaptureActive { get; init; }

    public bool HotkeyTestActive { get; init; }

    public int ConfiguredTestStrengthA { get; init; }

    public int ConfiguredTestStrengthB { get; init; }

    public int AppStrengthA { get; init; }

    public int AppStrengthB { get; init; }

    public int AppLimitA { get; init; }

    public int AppLimitB { get; init; }

    public string LastFeedback { get; init; } = string.Empty;

    public string LastError { get; init; } = string.Empty;

    public string LastNotice { get; init; } = string.Empty;

    public string LastEvent { get; init; } = string.Empty;

    public int TotalConnections { get; init; }

    public int TotalPairs { get; init; }
}
