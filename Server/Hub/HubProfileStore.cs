using System.Collections.Concurrent;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Hub;

internal static class HubProfileStore
{
    internal static void Replace(SaveServer saves, MongoId id, SptProfile expected, SptProfile replacement)
    {
        // GetProfiles returns a dictionary copy in SPT 4.1. Use the verified backing store to replace the whole staged profile atomically.
        var field = AccessTools.Field(typeof(SaveServer), "profiles");
        if (
            field?.GetValue(saves) is not ConcurrentDictionary<MongoId, SptProfile> profiles
            || !profiles.TryUpdate(id, replacement, expected)
        )
        {
            throw new InvalidOperationException("The profile changed while committing the campaign transaction.");
        }
    }
}
