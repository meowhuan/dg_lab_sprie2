using DgLabSocketSpire2.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace DgLabSocketSpire2.Patching;

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
internal static class BeforeCombatStartPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (runState.CurrentRoom is CombatRoom room)
            {
                BridgeService.Instance.OnCombatStarted(room.RoomType);
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
internal static class AfterCombatVictoryPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState, CombatRoom room)
    {
        __result = PatchHelpers.Chain(__result, () => BridgeService.Instance.OnCombatVictory());
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
internal static class AfterCombatEndPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState, CombatRoom room)
    {
        __result = PatchHelpers.Chain(__result, () => BridgeService.Instance.OnCombatEnded());
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
internal static class AfterDamageReceivedPatch
{
    private static void Postfix(ref Task __result, PlayerChoiceContext choiceContext, IRunState runState, CombatState? combatState, Creature target, DamageResult result, MegaCrit.Sts2.Core.ValueProps.ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (target.IsPlayer && LocalContext.IsMe(target))
            {
                BridgeService.Instance.OnPlayerDamageTaken(result.UnblockedDamage, dealer?.ToString() ?? "unknown");
                BridgeService.Instance.OnPlayerHpStateChanged(target.CurrentHp, target.MaxHp);
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged))]
internal static class AfterCurrentHpChangedPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState, Creature creature, decimal delta)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (creature.IsPlayer && LocalContext.IsMe(creature))
            {
                if (delta > 0)
                {
                    BridgeService.Instance.OnPlayerHealed((int)delta);
                }

                BridgeService.Instance.OnPlayerHpStateChanged(creature.CurrentHp, creature.MaxHp);
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDeath))]
internal static class AfterDeathPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (!wasRemovalPrevented && creature.IsPlayer && LocalContext.IsMe(creature))
            {
                BridgeService.Instance.OnPlayerDeath();
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
internal static class AfterCardPlayedPatch
{
    private static void Postfix(ref Task __result, CombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (cardPlay.Card.Owner != null && LocalContext.IsMe(cardPlay.Card.Owner.Creature))
            {
                BridgeService.Instance.OnCardPlayed(cardPlay.Card.Id.ToString());
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterGoldGained))]
internal static class AfterGoldGainedPatch
{
    private static readonly Dictionary<ulong, int> LastGold = new();

    private static void Postfix(ref Task __result, IRunState runState, Player player)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (!LocalContext.IsMe(player))
            {
                return;
            }

            var previous = LastGold.TryGetValue(player.NetId, out var value) ? value : player.Gold;
            var delta = Math.Max(0, player.Gold - previous);
            LastGold[player.NetId] = player.Gold;
            if (delta > 0)
            {
                BridgeService.Instance.OnGoldGained(delta);
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
internal static class AfterPotionUsedPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState, PotionModel potion, Creature? target)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (potion.Owner != null && LocalContext.IsMe(potion.Owner.Creature))
            {
                BridgeService.Instance.OnPotionUsed(potion.Id.ToString());
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionProcured))]
internal static class AfterPotionProcuredPatch
{
    private static void Postfix(ref Task __result, IRunState runState, CombatState? combatState, PotionModel potion)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (potion.Owner != null && LocalContext.IsMe(potion.Owner.Creature))
            {
                BridgeService.Instance.OnPotionObtained(potion.Id.ToString());
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRewardTaken))]
internal static class AfterRewardTakenPatch
{
    private static void Postfix(ref Task __result, IRunState runState, Player player, Reward reward)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (!LocalContext.IsMe(player))
            {
                return;
            }

            var type = reward switch
            {
                GoldReward => Configuration.BridgeEventType.RewardGoldTaken,
                PotionReward => Configuration.BridgeEventType.RewardPotionTaken,
                RelicReward => Configuration.BridgeEventType.RewardRelicTaken,
                CardReward => Configuration.BridgeEventType.RewardCardTaken,
                SpecialCardReward => Configuration.BridgeEventType.RewardCardTaken,
                _ => Configuration.BridgeEventType.RewardCardTaken
            };

            BridgeService.Instance.OnRewardTaken(type, $"Reward taken: {reward.GetType().Name}");
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterItemPurchased))]
internal static class AfterItemPurchasedPatch
{
    private static void Postfix(ref Task __result, IRunState runState, Player player, MerchantEntry itemPurchased, int goldSpent)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (LocalContext.IsMe(player))
            {
                BridgeService.Instance.OnMerchantPurchase($"Merchant purchase: {itemPurchased.GetType().Name}", goldSpent);
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockBroken))]
internal static class AfterBlockBrokenPatch
{
    private static void Postfix(ref Task __result, CombatState combatState, Creature creature)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (creature.IsPlayer && LocalContext.IsMe(creature))
            {
                BridgeService.Instance.OnBlockBroken();
            }
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class AfterPlayerTurnStartPatch
{
    private static void Postfix(ref Task __result, CombatState combatState, PlayerChoiceContext choiceContext, Player player)
    {
        __result = PatchHelpers.Chain(__result, () =>
        {
            if (LocalContext.IsMe(player))
            {
                BridgeService.Instance.OnTurnStart();
            }
        });
    }
}
