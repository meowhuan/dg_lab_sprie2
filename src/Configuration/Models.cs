using System.Text.Json.Serialization;

namespace DgLabSocketSpire2.Configuration;

public enum BridgeEventType
{
    PlayerHpLost,
    PlayerHealed,
    LowHealthEntered,
    LowHealthExited,
    CombatStart,
    EliteCombatStart,
    BossCombatStart,
    CombatVictory,
    PlayerDeath,
    CardPlayed,
    GoldGained,
    PotionUsed,
    PotionObtained,
    RewardCardTaken,
    RewardRelicTaken,
    RewardGoldTaken,
    RewardPotionTaken,
    MerchantPurchase,
    BlockBroken,
    TurnStart
}

public enum ChannelRef
{
    A = 1,
    B = 2
}

public enum TriggerMode
{
    Wave,
    Strength,
    Both
}

public enum ChannelUsageMode
{
    AOnly,
    BOnly,
    Both
}

public enum StrengthOperation
{
    Set,
    DeltaUp,
    DeltaDown,
    Clear
}

public sealed class ModConfig
{
    public ServerConfig Server { get; set; } = new();

    public SafetyConfig Safety { get; set; } = new();

    public UiConfig Ui { get; set; } = new();

    public string CurrentPreset { get; set; } = "Balanced";

    public Dictionary<string, PresetConfig> Presets { get; set; } = DefaultConfigFactory.CreatePresets();
}

public sealed class ServerConfig
{
    public int Port { get; set; } = 9999;

    public string PublicHost { get; set; } = string.Empty;

    public string PairPageRoute { get; set; } = "/pair";

    public bool AutoOpenPairPageOnStart { get; set; } = false;

    public int HeartbeatIntervalMs { get; set; } = 60000;

    public int DefaultPunishmentFrequency { get; set; } = 1;

    public int DefaultPunishmentDurationSeconds { get; set; } = 5;
}

public sealed class SafetyConfig
{
    public bool GlobalEnabled { get; set; } = true;

    public bool IgnoreEventsWhileUnbound { get; set; } = true;

    public bool AutoClearOnCombatEnd { get; set; } = true;

    public bool AutoClearOnDisconnect { get; set; } = true;

    public int GlobalCooldownMs { get; set; } = 150;

    public int LowHealthThresholdPercent { get; set; } = 30;
}

public sealed class PresetConfig
{
    public string DisplayName { get; set; } = "Preset";

    public string Description { get; set; } = string.Empty;

    public Dictionary<BridgeEventType, EventRuleConfig> Rules { get; set; } = new();
}

public sealed class UiConfig
{
    public HotkeyConfig ToggleHotkey { get; set; } = new();

    public int TestStrengthA { get; set; } = 35;

    public int TestStrengthB { get; set; } = 35;

    public WindowGeometryConfig ControlDialog { get; set; } = new();
}

public sealed class WindowGeometryConfig
{
    public bool HasSavedGeometry { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class HotkeyConfig
{
    public string Key { get; set; } = "F7";

    public bool Ctrl { get; set; }

    public bool Alt { get; set; }

    public bool Shift { get; set; }
}

public sealed class EventRuleConfig
{
    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TriggerMode Mode { get; set; } = TriggerMode.Both;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChannelRef Channel { get; set; } = ChannelRef.A;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChannelUsageMode ChannelUsage { get; set; } = ChannelUsageMode.AOnly;

    public int CooldownMs { get; set; } = 500;

    public int DurationSeconds { get; set; } = 1;

    public string Wave { get; set; } = "连击";

    public List<string> Waves { get; set; } = new();

    public bool RandomizeWaves { get; set; } = true;

    public int BaseStrength { get; set; } = 20;

    public int MaxStrength { get; set; } = 80;

    public double ScalePerUnit { get; set; } = 1.0;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StrengthOperation StrengthOperation { get; set; } = StrengthOperation.Set;

    public int RestoreAfterMs { get; set; } = 0;

    public bool ClearChannelBeforeWave { get; set; } = false;

    public bool OnlyInCombat { get; set; } = false;

    public bool RespectSoftLimit { get; set; } = true;
}

public sealed class BridgeEvent
{
    public BridgeEventType Type { get; init; }

    public double Magnitude { get; init; }

    public string Description { get; init; } = string.Empty;
}
