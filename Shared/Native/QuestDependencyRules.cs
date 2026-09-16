namespace WTT.Campaigns.Shared.Native;

/// <summary>Quest activation and completion are separate milestones.</summary>
public static class QuestDependencyRules
{
    public static IReadOnlyCollection<string> Cycles(IEnumerable<NativeQuest> definitions)
    {
        var quests = definitions.Where(q => q.SeasonalEnabled != false).ToDictionary(q => q.Id);
        var active = new HashSet<(string Id, bool Finished)>();
        var complete = new HashSet<(string Id, bool Finished)>();
        var cycles = new HashSet<string>();
        void Visit((string Id, bool Finished) node)
        {
            if (complete.Contains(node) || !quests.TryGetValue(node.Id, out var quest))
            {
                return;
            }
            if (!active.Add(node))
            {
                cycles.Add(node.Id);
                return;
            }
            if (node.Finished)
            {
                Visit((node.Id, false));
            }
            var conditions = node.Finished ? quest.Conditions.AvailableForFinish : quest.Conditions.AvailableForStart;
            foreach (
                var condition in conditions.Where(c => c.ConditionType == "Quest" && c.IsNecessary != false && c.Status is { Count: > 0 })
            )
            {
                // Any accepted pre-start or negative state means completion of
                // the referenced quest is not a mandatory prerequisite.
                var states = condition.Status!;
                if (!states.All(s => s is "2" or "3" or "4" or "Started" or "AvailableForFinish" or "Success"))
                {
                    continue;
                }
                var finished = states.All(s => s is "3" or "4" or "AvailableForFinish" or "Success");
                foreach (var target in condition.Target ?? new Serialization.StringTargets(Array.Empty<string>()))
                {
                    Visit((target, finished));
                }
            }
            active.Remove(node);
            complete.Add(node);
        }
        foreach (var quest in quests.Keys)
        {
            Visit((quest, true));
        }
        return cycles;
    }
}
