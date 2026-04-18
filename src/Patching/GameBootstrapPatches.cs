using DgLabSocketSpire2.Bridge;
using DgLabSocketSpire2.UI;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace DgLabSocketSpire2.Patching;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class MainMenuReadyPatch
{
    private static void Postfix(NMainMenu __instance)
    {
        BridgeUiHost.Instance.EnsureMainMenuButton(__instance, BridgeService.Instance);
        BridgeUiHost.Instance.EnsureDialog(BridgeService.Instance);
    }
}

[HarmonyPatch(typeof(NModdingScreen), nameof(NModdingScreen._Ready))]
internal static class ModdingScreenReadyPatch
{
    private static void Postfix(NModdingScreen __instance)
    {
        BridgeUiHost.Instance.EnsureModdingButton(__instance, BridgeService.Instance);
        BridgeUiHost.Instance.EnsureDialog(BridgeService.Instance);
    }
}
