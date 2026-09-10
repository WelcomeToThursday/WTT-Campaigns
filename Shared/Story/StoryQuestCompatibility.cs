using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Story;

public static class StoryQuestCompatibility
{
    private static readonly HashSet<string> CounterFilters = new(StringComparer.Ordinal)
    {
        "Location",
        "ExitStatus",
        "Kills",
        "ExitName",
        "InZone",
        "Time",
        "Equipment",
        "HealthEffect",
        "Shots",
        "UseItem",
        "TransitionLocation",
        "QuestTime",
    };

    public static readonly HashSet<string> ConditionTypes = new(StringComparer.Ordinal)
    {
        "Quest",
        "Level",
        "TraderLoyalty",
        "TraderStanding",
        "Skill",
        "HideoutArea",
        "FindItem",
        "HasItem",
        "HandoverItem",
        "CompleteCondition",
        "GlobalVariableValue",
        "CompletableItem",
        "LocationTrigger",
        "CounterCreator",
        "VisitPlace",
        "Location",
        "ExitStatus",
        "Kills",
        "LeaveItemAtLocation",
        "ExitName",
        "InZone",
        "Time",
        "LaunchFlare",
        "Equipment",
        "HealthEffect",
        "Shots",
        "UseItem",
        "TransitionLocation",
        "QuestTime",
    };

    public static bool Supports(NativeQuest quest, NativeCondition condition)
    {
        return Supports(
            condition,
            quest.AllConditions().Any(p => p.Counter?.Conditions.SelectMany(c => c.DescendantsAndSelf()).Contains(condition) == true)
        );
    }

    public static bool Supports(NativeCondition condition, bool nested = false)
    {
        var kind = (string?)condition.ConditionType ?? "";
        return ConditionTypes.Contains(kind) && (!CounterFilters.Contains(kind) || nested);
    }

    public static NativeQuest NativeTemplate(NativeQuest source)
    {
        var output = Seasons.SeasonCompiler.Copy(source);
        foreach (var condition in output.AllConditions())
        {
            if ((string?)condition.ConditionType is "CompletableItem" or "LocationTrigger")
            {
                // Beta's native variable checker has identical boolean comparison behavior.
                // Only the authoritative story snapshot supplies these variable values.
                condition.ConditionType = "GlobalVariableValue";
            }
        }
        return output;
    }
}
