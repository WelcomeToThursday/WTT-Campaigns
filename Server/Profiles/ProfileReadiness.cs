using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace WTT.Campaigns.Server.Profiles;

internal static class ProfileReadiness
{
    internal static PmcData? PlayablePmc(SptProfile? profile)
    {
        // Launcher wipes retain the old PMC until native creation replaces it.
        // Its nickname, inventory and appearance do not mean it can be selected.
        return profile?.ProfileInfo?.IsWiped == true ? null : profile?.CharacterData?.PmcData;
    }
}
