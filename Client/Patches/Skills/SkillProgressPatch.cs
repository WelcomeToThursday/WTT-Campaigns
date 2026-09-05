using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Skills;

internal class SkillProgressPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(BaseSkill), nameof(BaseSkill.SetCurrent));

    [PatchPrefix]
    private static void Prefix(BaseSkill __instance, ref float value)
    {
        var profile = Plugin.App?.Session?.Profile;
        if (
            Plugin.Current?.ActiveMode != "seasonal"
            || profile == null
            || !ReferenceEquals(profile.Skills.GetSkill(__instance.Id), __instance)
        )
        {
            return;
        }

        // Profile deserialization builds a new SkillManager before it is assigned to the session.
        // Only gains on the active character pass this reference check, so presets aren't scaled.
        var previous = __instance.Current;
        var skill = __instance.Id.ToString();
        if (value > previous)
        {
            value = Plugin.Effects.SkillBlocked(skill)
                ? previous
                : previous + (value - previous) * Plugin.Effects.SkillMultiplier(skill);
        }

        value = Math.Min(value, Plugin.Effects.SkillCap(skill) * 100f);
    }
}
