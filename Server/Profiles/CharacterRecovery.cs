using Newtonsoft.Json;

namespace WTT.Campaigns.Server.Profiles;

public sealed record RecoverableCharacter(string Id, string Name, string Campaign, string PreviousAccount, string PendingAccount);

public sealed record RecoveryAccount(string Id, string Name);

// Persisted before the first ownership write; retries always finish the same transfer.
public sealed class CharacterRecoveryOperation
{
    public string CharacterId { get; set; } = "";
    public string PreviousAccount { get; set; } = "";
    public string TargetAccount { get; set; } = "";
    public SeasonCharacterLink Character { get; set; } = new();
}

public static class CharacterRecovery
{
    public static void ValidateOwnership(
        string characterId,
        string previousAccount,
        string targetAccount,
        string? currentOwner,
        long revision,
        bool originalAccountExists,
        CharacterRecoveryOperation? pending
    )
    {
        if (originalAccountExists)
            throw new InvalidOperationException(
                "The original account still exists or is loaded. After deleting it in the launcher, restart SPT manually and refresh this page."
            );
        if (
            pending != null
            && (
                pending.CharacterId != characterId
                || pending.Character.ProfileId != characterId
                || pending.PreviousAccount != previousAccount
                || pending.TargetAccount != targetAccount
            )
        )
            throw new InvalidOperationException("An interrupted recovery must be completed to its original destination account.");
        if (revision <= 0 || (currentOwner != previousAccount && !(pending != null && currentOwner == targetAccount)))
            throw new InvalidOperationException("Character ownership changed. Refresh the page before recovering it.");
    }

    public static AccountLink Attach(AccountLink current, SeasonCharacterLink character)
    {
        if (!character.Created || character.Wiped)
            throw new InvalidOperationException("A wiped or unfinished character cannot be recovered.");
        var result = Clone(current);
        var existing = result.Characters.FirstOrDefault(c => c.ProfileId == character.ProfileId);
        if (existing != null && (existing.SeasonId != character.SeasonId || existing.Wiped || !existing.Created))
            throw new InvalidOperationException("The destination contains a conflicting character link.");
        if (result.RetiredCharacters.ContainsKey(character.ProfileId))
            throw new InvalidOperationException("The destination has retired this character.");
        if (existing == null)
            result.Characters.Add(Clone(character));
        return result;
    }

    public static AccountLink Detach(AccountLink current, string characterId)
    {
        var result = Clone(current);
        result.Characters.RemoveAll(c => c.ProfileId == characterId);
        foreach (var key in result.Seasons.Where(p => p.Value.ProfileId == characterId).Select(p => p.Key).ToArray())
            result.Seasons.Remove(key);
        result.RetiredCharacters.Remove(characterId);
        result.ActiveRaidProfiles.Remove(characterId);
        result.ActiveRaidIds.Remove(characterId);
        if (result.SeasonalId == characterId)
        {
            result.SeasonalId = null;
            result.CurrentSeasonId = null;
            result.Created = false;
            result.Mode = "normal";
        }
        return result;
    }

    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
}
