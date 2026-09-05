namespace SeasonalPerks.Shared;

public static class CharacterSession
{
    public static bool IsLoaded(Snapshot? snapshot, string mode, string? loadedProfileId)
    {
        return snapshot != null
            && snapshot.ActiveMode == mode
            && !string.IsNullOrEmpty(loadedProfileId)
            && loadedProfileId == snapshot.EffectiveProfileId;
    }
}
