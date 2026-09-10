using EFT;

namespace WTT.Campaigns.Client.Patches.Skills;

internal static class PmcExperience
{
    internal static float Multiplier(Profile? profile)
    {
        return
            profile != null
            && profile.Info.Side != EPlayerSide.Savage
            && Plugin.Current?.ActiveMode == "seasonal"
            && profile.Id == Plugin.App?.Session?.Profile?.Id
            ? Plugin.Effects.Multiplier("pmc_experience_multiplicator")
            : 1f;
    }
}
