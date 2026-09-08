using Newtonsoft.Json.Linq;

namespace SeasonalPerks.Shared.Story;

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

    public static bool Supports(JObject condition)
    {
        var kind = (string?)condition["conditionType"] ?? "";
        return ConditionTypes.Contains(kind)
            && (
                !CounterFilters.Contains(kind)
                || condition.Ancestors().OfType<JObject>().Any(parent => (string?)parent["conditionType"] == "CounterCreator")
            );
    }

    public static JObject NativeTemplate(JObject source)
    {
        var output = (JObject)source.DeepClone();
        foreach (var condition in output.Descendants().OfType<JObject>())
        {
            if ((string?)condition["conditionType"] is "CompletableItem" or "LocationTrigger")
            {
                // Beta's native variable checker has identical boolean comparison behavior.
                // Only the authoritative story snapshot supplies these variable values.
                condition["conditionType"] = "GlobalVariableValue";
            }
        }
        return output;
    }
}
