using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Story;

public static class StoryObservationRules
{
    public static void Validate(StoryRaidObservation observation, string character, StoryRaid? raid, ISet<string> conditions)
    {
        if (
            raid is not { Finished: false }
            || observation.RaidId != raid.Id
            || observation.CharacterId != character
            || observation.Sequence <= 0
            || observation.Items.Count > 4096
            || observation.Counters.Count > 20000
            || observation.CompletedConditions.Count > 20000
            || observation.Skills.Count > 200
            || observation.Level is < 1 or > 100
            || observation.FreeSpecialSlots is < 0 or > 20
        )
        {
            throw new InvalidOperationException("Invalid or expired raid observation.");
        }

        if (
            observation.Items.Select(i => i.Id).Distinct().Count() != observation.Items.Count
            || observation.Items.Any(i =>
                !SeasonValidator.IsId(i.Id ?? "")
                || !SeasonValidator.IsId(i.Template ?? "")
                || i.Data.Length > 32768
                || i.StackCount is < 1 or > 1000000000
            )
            || observation.Counters.Any(c => !conditions.Contains(c.Key) || !Finite(c.Value, int.MaxValue))
            || observation.CompletedConditions.Any(c => !conditions.Contains(c))
            || observation.Skills.Any(s => s.Key.Length is < 1 or > 64 || !Finite(s.Value, 100))
        )
        {
            throw new InvalidOperationException("The raid observation contains unknown or invalid facts.");
        }
    }

    private static bool Finite(double value, double maximum) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= maximum;

    public static void Apply(StoryFacts facts, StoryRaidObservation observation)
    {
        facts.Observation = observation;
        facts.Level = observation.Level;
        facts.FreeSpecialSlots = observation.FreeSpecialSlots;
        facts.Items = observation.Items.GroupBy(i => i.Template!).ToDictionary(g => g.Key, g => g.Sum(i => (double)i.StackCount));
        facts.CompletedConditions.ExceptWith(observation.Counters.Keys);
        foreach (var counter in observation.Counters)
        {
            facts.ConditionCounters[counter.Key] = counter.Value;
        }

        facts.CompletedConditions.UnionWith(observation.CompletedConditions);
        foreach (var skill in observation.Skills)
        {
            facts.Skills[skill.Key] = skill.Value;
        }
    }
}
