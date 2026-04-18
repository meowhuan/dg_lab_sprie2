using DgLabSocketSpire2.Bridge;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace DgLabSocketSpire2;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Initialize()
    {
        try
        {
            BridgeService.Instance.Start();
            _harmony = new Harmony("com.openai.dglab_socket_spire2");
            _harmony.PatchAll();
            ModLog.Info("Initialized.");
        }
        catch (Exception ex)
        {
            ModLog.Error("Initialization failed.", ex);
            GD.PrintErr("[DG-LAB Socket] Initialization failed: ", ex);
        }
    }
}
