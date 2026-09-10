using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Eft.Profile;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Effects;
using WTT.Campaigns.Shared.Effects.Skills;

namespace WTT.Campaigns.Server.Patches.Skills;

[Injectable]
public class ExamineExperiencePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryController), "FlagItemsAsInspectedAndRewardXp");
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(SptProfile fullProfile, out int? __state)
    {
        __state = fullProfile.CharacterData?.PmcData?.Info?.Experience;
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(SptProfile fullProfile, int? __state)
    {
        var pmc = fullProfile.CharacterData?.PmcData;
        if (__state is not { } previous || pmc?.Info?.Experience is not { } current)
        {
            return;
        }

        var effects = ServerStartup.Seasons.Effects(pmc);
        pmc.Info.Experience = ExperienceScaling.Total(previous, current, effects.Multiplier("pmc_experience_multiplicator"));
    }
}
