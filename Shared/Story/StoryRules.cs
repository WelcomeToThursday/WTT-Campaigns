namespace SeasonalPerks.Shared.Story;

public static class StoryRules
{
    public static readonly HashSet<string> ConditionTypes = new(StringComparer.Ordinal)
    {
        "All",
        "Any",
        "Not",
        "VariableValue",
        "QuestStatus",
        "QuestConditionStatus",
        "CompleteCondition",
        "Level",
        "TraderReputation",
        "TraderLoyalty",
        "HasItem",
        "HasItemForHandover",
        "HasFreeSpecialSlot",
        "ServiceAvailable",
        "HasNewQuests",
        "CurrentTrader",
        "CompletableItem",
        "LocationTrigger",
        "Location",
        "Skill",
        "HideoutArea",
    };

    public static readonly HashSet<string> Operators = new(StringComparer.Ordinal) { "==", "!=", ">", ">=", "<", "<=" };

    public static readonly HashSet<string> QuestStatuses = new(StringComparer.Ordinal)
    {
        "Locked",
        "AvailableForStart",
        "Started",
        "AvailableForFinish",
        "Success",
        "Fail",
        "FailRestartable",
        "MarkedAsFailed",
        "Expired",
        "AvailableAfter",
    };

    public static bool Compare(double actual, string operation, double expected)
    {
        if (double.IsNaN(actual) || double.IsInfinity(actual) || double.IsNaN(expected) || double.IsInfinity(expected))
        {
            return false;
        }
        return operation switch
        {
            "==" => actual.Equals(expected),
            "!=" => !actual.Equals(expected),
            ">" => actual > expected,
            ">=" => actual >= expected,
            "<" => actual < expected,
            "<=" => actual <= expected,
            _ => throw new InvalidOperationException("Unsupported story comparison: " + operation),
        };
    }

    public static int Variable(StoryDefinition definition, StoryProgress state, StoryFacts facts, string id)
    {
        var variable =
            definition.Variables.SingleOrDefault(v => v.Id == id) ?? throw new InvalidOperationException("Unknown story variable: " + id);
        var values = variable.Scope switch
        {
            StoryVariableScope.Profile => state.Variables,
            StoryVariableScope.Session => facts.SessionVariables,
            StoryVariableScope.Dialogue => state.Conversation?.Variables,
            _ => throw new InvalidOperationException("Unsupported variable scope."),
        };
        return values != null && values.TryGetValue(id, out var value) ? value : variable.InitialValue;
    }

    public static bool Evaluate(StoryCondition condition, StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        return Evaluate(condition, definition, state, facts, 0);
    }

    private static bool Evaluate(StoryCondition c, StoryDefinition definition, StoryProgress state, StoryFacts facts, int depth)
    {
        if (depth > 32 || !ConditionTypes.Contains(c.Type))
        {
            throw new InvalidOperationException("Invalid or excessively nested story condition.");
        }
        if (c.InRaidOnly && !facts.InRaid)
        {
            return false;
        }
        bool CompareValue(double value)
        {
            return Compare(value, c.Operator, c.Value);
        }
        return c.Type switch
        {
            "All" => c.Conditions.All(child => Evaluate(child, definition, state, facts, depth + 1)),
            "Any" => c.Conditions.Any(child => Evaluate(child, definition, state, facts, depth + 1)),
            "Not" => c.Conditions.Count == 1 && !Evaluate(c.Conditions[0], definition, state, facts, depth + 1),
            "VariableValue" => CompareValue(Variable(definition, state, facts, c.Target)),
            "QuestStatus" => c.Status.Contains(facts.QuestStatuses.GetValueOrDefault(c.Target) ?? "Locked"),
            "QuestConditionStatus" or "CompleteCondition" => CompareValue(facts.CompletedConditions.Contains(c.Target) ? 1 : 0),
            "Level" => CompareValue(facts.Level),
            "TraderReputation" => CompareValue(facts.TraderReputation.GetValueOrDefault(c.Target)),
            "TraderLoyalty" => CompareValue(facts.TraderLoyalty.GetValueOrDefault(c.Target)),
            "HasItem" => CompareValue(facts.Items.GetValueOrDefault(c.Target)),
            "HasItemForHandover" => CompareValue(facts.HandoverItems.GetValueOrDefault(c.Target)),
            "HasFreeSpecialSlot" => CompareValue(facts.FreeSpecialSlots),
            "ServiceAvailable" => CompareValue(facts.AvailableServices.Contains(c.Target) ? 1 : 0),
            "HasNewQuests" => CompareValue(facts.TradersWithNewQuests.Contains(c.Target) ? 1 : 0),
            "CurrentTrader" => facts.TraderId == c.Target,
            "CompletableItem" => CompareValue(state.CompletedItems.Contains(c.Target) ? 1 : 0),
            "LocationTrigger" => CompareValue(state.CompletedBindings.Contains(c.Target) ? 1 : 0),
            "Location" => facts.InRaid && facts.Location == c.Target,
            "Skill" => CompareValue(facts.Skills.GetValueOrDefault(c.Target)),
            "HideoutArea" => CompareValue(facts.HideoutAreas.GetValueOrDefault(c.Target)),
            _ => throw new InvalidOperationException("Unsupported story condition: " + c.Type),
        };
    }

    public static IReadOnlyList<StoryDialogLine> EligibleLines(StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        var conversation = state.Conversation;
        if (conversation == null || conversation.Closed)
        {
            return Array.Empty<StoryDialogLine>();
        }
        var dialog = definition.Dialogs.Single(d => d.Id == conversation.DialogId);
        return dialog
            .Lines.Where(line => Evaluate(line.Trigger, definition, state, facts) && RandomMatches(line.Random, conversation))
            .ToArray();
    }

    public static bool RandomMatches(StoryRandomGate? gate, StoryConversation conversation)
    {
        if (gate == null)
        {
            return true;
        }
        return conversation.RandomValues.TryGetValue(gate.VariableId + ":" + gate.Group, out var value)
            && value >= gate.Start
            && value <= gate.End;
    }

    public static bool ChapterComplete(StoryChapter chapter, StoryDefinition definition, StoryFacts facts)
    {
        var required = definition.Quests.Where(q => q.ChapterId == chapter.Id && q.Main).ToArray();
        return required.Length > 0 && required.All(q => facts.QuestStatuses.GetValueOrDefault(q.QuestId) == "Success");
    }
}
