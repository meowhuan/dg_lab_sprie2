namespace DgLabSocketSpire2.Configuration;

internal static class DefaultConfigFactory
{
    public static Dictionary<string, PresetConfig> CreatePresets()
    {
        return new Dictionary<string, PresetConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["Balanced"] = CreateBalanced(),
            ["CombatHeavy"] = CreateCombatHeavy(),
            ["RewardHeavy"] = CreateRewardHeavy(),
            ["Minimal"] = CreateMinimal()
        };
    }

    private static PresetConfig CreateBalanced()
    {
        return new PresetConfig
        {
            DisplayName = "Balanced",
            Description = "以掉血、濒死、胜利、死亡和关键奖励为主，反馈节奏克制，适合长期游玩。",
            Rules = new Dictionary<BridgeEventType, EventRuleConfig>
            {
                [BridgeEventType.PlayerHpLost] = Wave(ChannelRef.A, "连击", 1, 400, clearBeforeWave: true),
                [BridgeEventType.LowHealthEntered] = Strength(ChannelRef.B, 24, 48, 0, StrengthOperation.Set),
                [BridgeEventType.LowHealthExited] = Strength(ChannelRef.B, 0, 0, 0, StrengthOperation.Clear),
                [BridgeEventType.CombatStart] = Wave(ChannelRef.B, "呼吸", 1, 1200),
                [BridgeEventType.EliteCombatStart] = Wave(ChannelRef.B, "心跳节奏", 2, 1500, clearBeforeWave: true),
                [BridgeEventType.BossCombatStart] = Wave(ChannelRef.B, "压缩", 2, 1500, clearBeforeWave: true),
                [BridgeEventType.CombatVictory] = Wave(ChannelRef.A, "潮汐", 1, 1200, clearBeforeWave: true),
                [BridgeEventType.PlayerDeath] = Wave(ChannelRef.A, "变速敲击", 2, 3000, clearBeforeWave: true),
                [BridgeEventType.RewardGoldTaken] = Wave(ChannelRef.A, "快速按捏", 1, 600),
                [BridgeEventType.RewardPotionTaken] = Wave(ChannelRef.A, "按捏渐强", 1, 600),
                [BridgeEventType.RewardRelicTaken] = Wave(ChannelRef.B, "节奏步伐", 1, 800),
                [BridgeEventType.RewardCardTaken] = Wave(ChannelRef.A, "呼吸", 1, 700),
                [BridgeEventType.PotionUsed] = Wave(ChannelRef.B, "颗粒摩擦", 1, 500)
            }
        };
    }

    private static PresetConfig CreateCombatHeavy()
    {
        var preset = CreateBalanced();
        preset.DisplayName = "Combat Heavy";
        preset.Description = "在平衡预设上加入打牌、回合开始、破防和治疗反馈，战斗内触发更密集。";
        preset.Rules[BridgeEventType.CardPlayed] = Wave(ChannelRef.A, "快速按捏", 1, 350);
        preset.Rules[BridgeEventType.BlockBroken] = Wave(ChannelRef.B, "按捏渐强", 1, 600);
        preset.Rules[BridgeEventType.TurnStart] = Wave(ChannelRef.B, "呼吸", 1, 800);
        preset.Rules[BridgeEventType.PlayerHealed] = Wave(ChannelRef.A, "潮汐", 1, 800);
        return preset;
    }

    private static PresetConfig CreateRewardHeavy()
    {
        var preset = CreateBalanced();
        preset.DisplayName = "Reward Heavy";
        preset.Description = "弱化战斗内高频事件，强化金币、药水、奖励和商店购买等战斗外反馈。";
        preset.Rules.Remove(BridgeEventType.CardPlayed);
        preset.Rules[BridgeEventType.GoldGained] = Wave(ChannelRef.A, "快速按捏", 1, 450);
        preset.Rules[BridgeEventType.PotionObtained] = Wave(ChannelRef.B, "按捏渐强", 1, 600);
        preset.Rules[BridgeEventType.MerchantPurchase] = Wave(ChannelRef.B, "潮汐", 1, 700);
        return preset;
    }

    private static PresetConfig CreateMinimal()
    {
        return new PresetConfig
        {
            DisplayName = "Minimal",
            Description = "仅保留掉血、胜利和死亡三类核心触发，用于最稳的基础链路。",
            Rules = new Dictionary<BridgeEventType, EventRuleConfig>
            {
                [BridgeEventType.PlayerHpLost] = Wave(ChannelRef.A, "连击", 1, 500, clearBeforeWave: true),
                [BridgeEventType.CombatVictory] = Wave(ChannelRef.B, "呼吸", 1, 1200, clearBeforeWave: true),
                [BridgeEventType.PlayerDeath] = Wave(ChannelRef.A, "变速敲击", 2, 3000, clearBeforeWave: true)
            }
        };
    }

    private static EventRuleConfig Wave(ChannelRef channel, string wave, int durationSeconds, int cooldownMs, bool clearBeforeWave = false)
    {
        return new EventRuleConfig
        {
            Mode = TriggerMode.Wave,
            Channel = channel,
            ChannelUsage = channel == ChannelRef.A ? ChannelUsageMode.AOnly : ChannelUsageMode.BOnly,
            Wave = wave,
            Waves = new List<string> { wave },
            DurationSeconds = durationSeconds,
            CooldownMs = cooldownMs,
            ClearChannelBeforeWave = clearBeforeWave
        };
    }

    private static EventRuleConfig Strength(ChannelRef channel, int baseStrength, int maxStrength, int restoreAfterMs, StrengthOperation operation)
    {
        return new EventRuleConfig
        {
            Mode = TriggerMode.Strength,
            Channel = channel,
            ChannelUsage = channel == ChannelRef.A ? ChannelUsageMode.AOnly : ChannelUsageMode.BOnly,
            BaseStrength = baseStrength,
            MaxStrength = maxStrength,
            CooldownMs = 1000,
            StrengthOperation = operation,
            RestoreAfterMs = restoreAfterMs
        };
    }
}
