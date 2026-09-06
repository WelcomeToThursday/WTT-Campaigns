using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Effects.Skills;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace SeasonalPerks.Server.Patches.Skills;

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

        var effects = new RuntimeEffects(ServerStartup.Seasons.Catalogue, SeasonService.State(pmc).SeasonalPerks);
        pmc.Info.Experience = ExperienceScaling.Total(previous, current, effects.Multiplier("pmc_experience_multiplicator"));
    }
}
