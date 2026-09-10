using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Effects;

namespace WTT.Campaigns.Server.Patches.Hideout;

[Injectable]
public class CraftTimePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(HideoutHelper), nameof(HideoutHelper.GetAdjustedCraftTimeWithSkills));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(PmcData pmcData, ref double? __result)
    {
        var effects = ServerStartup.Seasons.Effects(pmcData);
        if (__result.HasValue && effects.Has("craft_time_multiplicator"))
        {
            __result = Math.Max(5, __result.Value * effects.Multiplier("craft_time_multiplicator"));
        }
    }
}
