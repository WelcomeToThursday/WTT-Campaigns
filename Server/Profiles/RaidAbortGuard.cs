namespace WTT.Campaigns.Server.Profiles;

internal static class RaidAbortGuard
{
    internal static bool Matches(AccountLink link, string activeCharacter, string character, string raidId)
    {
        return character == activeCharacter
            && !string.IsNullOrEmpty(raidId)
            && link.ActiveRaidProfiles.Contains(character)
            && link.ActiveRaidIds.TryGetValue(character, out var expected)
            && expected == raidId;
    }
}
