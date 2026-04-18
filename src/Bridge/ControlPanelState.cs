using DgLabSocketSpire2.Configuration;

namespace DgLabSocketSpire2.Bridge;

public sealed class ControlPanelState
{
    public BridgeStatus Status { get; init; } = new();

    public ControlPanelGlobalSettings GlobalSettings { get; init; } = new();

    public List<ControlPanelPresetOption> Presets { get; init; } = new();

    public List<ControlPanelEventOption> Events { get; init; } = new();

    public List<ControlPanelRuleEntry> Rules { get; init; } = new();

    public List<string> Waves { get; init; } = new();
}

public sealed class ControlPanelGlobalSettings
{
    public bool GlobalEnabled { get; init; }

    public bool IgnoreEventsWhileUnbound { get; init; }

    public bool AutoClearOnCombatEnd { get; init; }

    public bool AutoClearOnDisconnect { get; init; }

    public int GlobalCooldownMs { get; init; }

    public int LowHealthThresholdPercent { get; init; }

    public string PublicHostOverride { get; init; } = string.Empty;
}

public sealed class ControlPanelPresetOption
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

public sealed class ControlPanelEventOption
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

public sealed class ControlPanelRuleEntry
{
    public string EventType { get; init; } = string.Empty;

    public EventRuleConfig Rule { get; init; } = new();
}
