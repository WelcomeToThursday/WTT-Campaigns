namespace WTT.Campaigns.Server.Profiles;

public sealed partial class SeasonService
{
    // Call under the account lease, alongside the descriptor lookup.
    internal bool HasActiveMapLayerRaid(string character, string raidId)
    {
        var link = Link(ResolveRoot(character));
        return !string.IsNullOrEmpty(raidId)
            && link.ActiveRaidProfiles.Contains(character)
            && link.ActiveRaidIds.TryGetValue(character, out var active)
            && active == raidId;
    }
}
