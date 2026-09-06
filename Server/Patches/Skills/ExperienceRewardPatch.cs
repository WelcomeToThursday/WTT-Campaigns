using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Shared.Effects.Skills;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;

namespace SeasonalPerks.Server.Patches.Skills;

[Injectable]
public class ExperienceRewardPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ProfileHelper), nameof(ProfileHelper.AddExperienceToPmc));

    [PatchPrefix]
    private static void Prefix(MongoId sessionId, ref int experienceToAdd) =>
        experienceToAdd = ExperienceScaling.Award(
            experienceToAdd,
            ServerStartup.Seasons.Effects(sessionId.ToString()).Multiplier("pmc_experience_multiplicator")
        );
}
