using System;
using System.Collections.Generic;
using ZLinq;

namespace WTT.Campaigns.Shared.Hub;

public static class HubRules
{
    public const int DocumentLimit = 30;
    public const int WindowSeconds = 23 * 60 * 60;

    public static int Remaining(HubProgress state, long now, int limit = DocumentLimit, int window = WindowSeconds)
    {
        return state.WindowStart == 0 || now >= state.WindowStart + window ? limit : Math.Max(0, limit - state.Pickups);
    }

    public static bool Pickup(
        HubProgress state,
        HubRaid raid,
        string itemId,
        long now,
        int limit = DocumentLimit,
        int window = WindowSeconds
    )
    {
        if (raid.Finished || !raid.Spawned.ContainsKey(itemId) || raid.Rejected.Contains(itemId))
        {
            return false;
        }

        if (raid.Picked.Contains(itemId))
        {
            return true;
        }

        if (Remaining(state, now, limit, window) == 0)
        {
            raid.Rejected.Add(itemId);
            return false;
        }
        if (state.WindowStart == 0 || now >= state.WindowStart + window)
        {
            state.WindowStart = now;
            state.Pickups = 0;
        }
        raid.Picked.Add(itemId);
        state.Pickups++;
        return true;
    }

    public static int Finish(
        HubProgress state,
        HubRaid raid,
        IEnumerable<string> extractedIds,
        bool survived,
        Func<int> roll,
        int chance = 5
    )
    {
        if (raid.Finished)
        {
            return raid.Bonus;
        }

        raid.Finished = true;
        if (survived)
        {
            foreach (var id in extractedIds.AsValueEnumerable().Distinct())
            {
                if (raid.Picked.Contains(id) && raid.Spawned.ContainsKey(id) && roll() < chance)
                {
                    raid.Bonus++;
                }
            }
        }
        state.Classified = checked(state.Classified + raid.Bonus);
        return raid.Bonus;
    }

    public static int Shortage(IEnumerable<KeyValuePair<string, int>> costs, IReadOnlyDictionary<string, int> owned)
    {
        return costs.AsValueEnumerable().Sum(c => Math.Max(0, c.Value - (owned.TryGetValue(c.Key, out var count) ? count : 0)));
    }
}
