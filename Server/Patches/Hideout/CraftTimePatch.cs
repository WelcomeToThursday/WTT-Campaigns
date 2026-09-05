using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace SeasonalPerks.Server.Patches.Hideout;

[Injectable]
public class CraftTimePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(HideoutHelper),
            nameof(HideoutHelper.GetAdjustedCraftTimeWithSkills)
        );

    [PatchPostfix]
    private static void Postfix(PmcData pmcData, ref double? __result)
    {
        var effects = new RuntimeEffects(
            ServerStartup.Seasons.Catalogue,
            SeasonService.State(pmcData).SeasonalPerks
        );
        if (__result.HasValue && effects.Has("craft_time_multiplicator"))
        {
            __result = Math.Max(5, __result.Value * effects.Multiplier("craft_time_multiplicator"));
        }
    }
}
