using System.Security.Cryptography;
using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;

namespace WTT.Campaigns.Server.Profiles;

public sealed partial class SeasonService
{
    private static string RecoveryDirectory => Path.GetFullPath("user/seasonal/recovery");

    private static string RecoveryPath(string id) => Path.Combine(RecoveryDirectory, id + ".json");

    private bool AccountExists(string id) =>
        MongoId.IsValidMongoId(id)
        && (saves.ProfileExists(new MongoId(id)) || File.Exists(Path.GetFullPath(Path.Combine("user/profiles", id + ".json"))));

    public List<RecoveryAccount> RecoveryAccounts() =>
        saves
            .GetProfiles()
            .Where(p =>
                !SeasonProfileStorage.Contains(p.Key.ToString())
                && !_ephemeralLinks.ContainsKey(p.Key.ToString())
                && File.Exists(Path.GetFullPath(Path.Combine("user/profiles", p.Key + ".json")))
                && p.Value.ProfileInfo != null
            )
            .Select(p => new RecoveryAccount(p.Key.ToString(), p.Value.ProfileInfo!.Username ?? p.Key.ToString()))
            .OrderBy(p => p.Name)
            .ToList();

    public List<RecoverableCharacter> RecoverableCharacters()
    {
        var result = new List<RecoverableCharacter>();
        foreach (var pair in saves.GetProfiles())
        {
            var id = pair.Key.ToString();
            if (!SeasonProfileStorage.Contains(id) || _ephemeralLinks.ContainsKey(id))
                continue;
            var pmc = ProfileReadiness.PlayablePmc(pair.Value);
            if (pmc?.Info == null)
                continue;
            var state = State(pmc);
            var pending = ReadRecovery(id);
            var owner = pending?.PreviousAccount ?? state.RootAccountId;
            if (state.Revision <= 0 || !MongoId.IsValidMongoId(owner) || owner == id || AccountExists(owner!))
                continue;
            result.Add(
                new(id, pmc.Info.Nickname ?? id, state.SeasonId ?? Seasons.SeasonRepository.LegacyId, owner!, pending?.TargetAccount ?? "")
            );
        }
        return result.OrderBy(c => c.Name).ToList();
    }

    private static CharacterRecoveryOperation? ReadRecovery(string id) =>
        File.Exists(RecoveryPath(id))
            ? JsonConvert.DeserializeObject<CharacterRecoveryOperation>(File.ReadAllText(RecoveryPath(id)))
                ?? throw new InvalidDataException("The character recovery record is unreadable.")
            : null;

    public async Task RecoverCharacter(string characterId, string previousAccount, string targetAccount)
    {
        if (
            !MongoId.IsValidMongoId(characterId)
            || !MongoId.IsValidMongoId(previousAccount)
            || !MongoId.IsValidMongoId(targetAccount)
            || characterId == previousAccount
            || characterId == targetAccount
            || previousAccount == targetAccount
        )
            throw new InvalidOperationException("Select a saved character and a different launcher account.");

        // The character lock serializes competing website requests; account locks also
        // exclude the normal create/switch/wipe operations while their links are updated.
        using var characterLease = Enter(characterId);
        var ordered = new[] { previousAccount, targetAccount }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        using var first = Enter(ordered[0]);
        using var second = Enter(ordered[1]);
        if (AccountExists(previousAccount))
            throw new InvalidOperationException(
                "The original account still exists or is loaded. After deleting it in the launcher, restart SPT manually and refresh this page."
            );
        if (!RecoveryAccounts().Any(a => a.Id == targetAccount))
            throw new InvalidOperationException("The destination launcher account is no longer available. Refresh the page.");
        if (!SeasonProfileStorage.Contains(characterId) || !saves.ProfileExists(new MongoId(characterId)))
            throw new InvalidOperationException("The saved campaign character is unavailable.");
        var profile = saves.GetProfile(new MongoId(characterId));
        var pmc = ProfileReadiness.PlayablePmc(profile);
        if (pmc?.Info == null)
            throw new InvalidOperationException("This character has no recoverable PMC save.");
        var state = State(pmc);
        var pending = ReadRecovery(characterId);
        CharacterRecovery.ValidateOwnership(
            characterId,
            previousAccount,
            targetAccount,
            state.RootAccountId,
            state.Revision,
            AccountExists(previousAccount),
            pending
        );
        var source = Link(previousAccount);
        // Stale raid flags on a deleted account are retained until the administrator
        // recovers it. The client must be closed; no automatic process handling occurs.
        var destination = Link(targetAccount);
        var destinationProfile = saves.GetProfile(new MongoId(targetAccount));
        if (
            destination.ActiveRaidProfiles.Count > 0
            || (destinationProfile.InraidData?.Location is { Length: > 0 } location && location != "none")
        )
            throw new InvalidOperationException("Finish the destination account's raid before recovering a character.");
        var entry =
            pending?.Character
            ?? source.Characters.FirstOrDefault(c => c.ProfileId == characterId)
            ?? new SeasonCharacterLink
            {
                ProfileId = characterId,
                SeasonId = state.SeasonId ?? Seasons.SeasonRepository.LegacyId,
                Created = true,
                Name = pmc.Info.Nickname ?? "",
            };
        if (!entry.Created || entry.Wiped || source.RetiredCharacters.ContainsKey(characterId))
            throw new InvalidOperationException("A wiped or retired character cannot be recovered.");
        var attached = CharacterRecovery.Attach(destination, entry);
        var detached = CharacterRecovery.Detach(source, characterId);
        if (pending == null)
        {
            Directory.CreateDirectory(RecoveryDirectory);
            var backup = Path.Combine(RecoveryDirectory, "backups", characterId + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            BackupRecoveryFile(
                Path.Combine(SeasonProfileStorage.DirectoryPath, characterId + ".json"),
                Path.Combine(backup, "character.json"),
                true
            );
            foreach (var account in new[] { previousAccount, targetAccount })
                BackupRecoveryFile(
                    Path.GetFullPath(Path.Combine("user/profileData", account, LinkKey + ".json")),
                    Path.Combine(backup, account + ".json"),
                    false
                );
            pending = new()
            {
                CharacterId = characterId,
                PreviousAccount = previousAccount,
                TargetAccount = targetAccount,
                Character = entry,
            };
            var temporary = RecoveryPath(characterId) + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(pending, Formatting.Indented));
            File.Move(temporary, RecoveryPath(characterId), true);
        }
        // Ownership is committed last. Until then the journal and original owner
        // make a partial transfer discoverable and safely retryable after restart.
        await profileData.SaveProfileDataAsync(new MongoId(targetAccount), LinkKey, attached);
        _links[targetAccount] = attached;
        await profileData.SaveProfileDataAsync(new MongoId(previousAccount), LinkKey, detached);
        _links[previousAccount] = detached;
        state.RootAccountId = targetAccount;
        SetState(pmc, state);
        if (profile.InraidData != null)
            profile.InraidData.Location = "none";
        await saves.SaveProfileAsync(new MongoId(characterId));
        File.Delete(RecoveryPath(characterId));
    }

    private static void BackupRecoveryFile(string source, string destination, bool required)
    {
        if (!File.Exists(source))
        {
            if (required)
                throw new IOException("The character save is missing on disk; recovery was not started.");
            return;
        }
        var bytes = File.ReadAllBytes(source);
        File.WriteAllBytes(destination, bytes);
        if (!SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination))))
            throw new IOException("Character recovery backup verification failed.");
    }
}
