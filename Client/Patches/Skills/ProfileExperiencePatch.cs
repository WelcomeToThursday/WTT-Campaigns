using System.Reflection;
using EFT;
using HarmonyLib;
using SeasonalPerks.Shared.Effects.Skills;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Skills;

// ExamineOperation and ItemManipulator.FinishConditional award through this wrapper.
// Backend reconciliation and deserialization write ProfileInfo directly instead.
public class ProfileExperiencePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertySetter(typeof(Profile), nameof(Profile.Experience));
    }

    [PatchPrefix]
    private static void Prefix(Profile __instance, ref int value)
    {
        value = ExperienceScaling.Total(__instance.Experience, value, PmcExperience.Multiplier(__instance));
    }
}
