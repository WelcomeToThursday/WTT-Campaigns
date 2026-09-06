using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Skills;

public class TreatmentExperiencePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(OfflineStatisticManager),
            nameof(OfflineStatisticManager.ExperienceGained)
        );

    [PatchPrefix]
    private static void Prefix(OfflineStatisticManager __instance, ref float experience)
    {
        if (experience > 0)
            experience *= PmcExperience.Multiplier(__instance.Profile);
    }
}
