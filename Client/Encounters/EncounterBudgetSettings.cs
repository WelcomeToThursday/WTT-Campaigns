using BepInEx.Configuration;
using UnityEngine;

namespace WTT.Campaigns.Client.Encounters;

internal static class EncounterBudgetSettings
{
    private static ConfigEntry<int>? _active,
        _waves,
        _navigation;
    internal static int ActiveBots =>
        (
            _active ??= Bind(
                "Maximum active bots",
                64,
                1,
                256,
                "Maximum living authored encounter bots, including reservations for spawning waves. Waves queue intact; larger individual waves are rejected before preview or mission activation."
            )
        ).Value;
    internal static int ConcurrentWaves =>
        (
            _waves ??= Bind(
                "Concurrent spawning waves",
                1,
                1,
                4,
                "Maximum authored waves generating profiles or activating bots concurrently. Native activation starts are also limited to one per frame."
            )
        ).Value;
    internal static int NavigationChecks =>
        (
            _navigation ??= Bind(
                "Navigation checks per frame",
                8,
                2,
                64,
                "Authored patrol planning and hold/patrol movement checks per frame. Half is reserved for each. Does not limit SAIN combat or mandatory spawn validation."
            )
        ).Value;

    private static ConfigEntry<int> Bind(string key, int value, int min, int max, string description) =>
        Plugin.Instance.Config.Bind("Campaign AI", key, value, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
}

// One authored runtime owns these quotas; optional AI layers share them with one another.
internal static class EncounterNavigationBudget
{
    private static EncounterFrameBudget _planning = new(4),
        _movement = new(4);

    internal static void Begin(int total)
    {
        _planning = new(total / 2);
        _movement = new(total - total / 2);
    }

    internal static bool Plan() => _planning.TryTake(Time.frameCount);

    internal static bool CanPlan => _planning.HasCapacity(Time.frameCount);

    internal static bool Move() => _movement.TryTake(Time.frameCount);
}
