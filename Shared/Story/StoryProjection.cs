namespace WTT.Campaigns.Shared.Story;

public static class StoryProjection
{
    public static Dictionary<string, int> Variables(StoryDefinition definition, StoryProgress state)
    {
        var values = definition
            .Variables.AsValueEnumerable()
            .Where(v => v.Scope == StoryVariableScope.Profile)
            .ToDictionary(v => v.Id, v => state.Variables.GetValueOrDefault(v.Id, v.InitialValue));
        foreach (var binding in definition.RaidBindings)
        {
            values[binding.Id] = state.CompletedBindings.Contains(binding.Id) ? 1 : 0;
        }

        var targets = definition
            .RaidBindings.AsValueEnumerable()
            .Where(b => b.Kind == "Collectible")
            .Select(b => b.ItemId)
            .Concat(
                definition
                    .Dialogs.AsValueEnumerable()
                    .SelectMany(d => d.Lines)
                    .SelectMany(l => l.Actions)
                    .Concat(definition.RaidBindings.AsValueEnumerable().SelectMany(b => b.Actions))
                    .Where(a => a.Type == StoryActionType.CompleteItem)
                    .Select(a => a.Target)
            )
            .ToArray();
        foreach (var target in targets.AsValueEnumerable().Where(t => t.Length > 0).Distinct())
        {
            values[target] = state.CompletedItems.Contains(target) ? 1 : 0;
        }

        return values;
    }

    public static bool EntryAvailable(StoryEntryPoint entry, StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        if ((entry.Kind == "InLobby") == facts.InRaid || entry.Scene.Length > 0 && entry.Scene != facts.Scene)
        {
            return false;
        }

        var previous = facts.TraderId;
        try
        {
            facts.TraderId = entry.TraderId;
            return StoryRules.Evaluate(entry.Condition, definition, state, facts);
        }
        finally
        {
            facts.TraderId = previous;
        }
    }
}
