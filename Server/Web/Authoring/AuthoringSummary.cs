using System.Globalization;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Web.Authoring;

// Summaries describe saved rules, never infer gameplay from the author's objective text.
public static class AuthoringSummary
{
    public static string ObjectiveKind(string kind)
    {
        return kind switch
        {
            "CounterCreator" => "Count raid events",
            "HandoverItem" => "Hand over items",
            "FindItem" => "Find items",
            "HasItem" => "Possess items",
            "Quest" => "Previous quest",
            "Level" => "Player level",
            "TraderLoyalty" => "Trader loyalty",
            "VisitPlace" => "Visit a location",
            "LeaveItemAtLocation" => "Place items",
            _ => EditorFieldGuide.NativeTitle(kind),
        };
    }

    public static string Status(string status)
    {
        return status switch
        {
            "0" or "Locked" => "locked",
            "1" or "AvailableForStart" => "available to accept",
            "2" or "Started" => "accepted",
            "3" or "AvailableForFinish" => "ready to hand in",
            "4" or "Success" => "completed and handed in",
            "5" or "Fail" => "failed",
            _ => StoryAuthoring.Friendly(status),
        };
    }

    public static string Objective(SeasonDefinition season, NativeCondition condition, Func<string, string, string> installed)
    {
        var kind = condition.ConditionType;
        var targetKind =
            kind is "Quest" ? "quests"
            : kind is "TraderLoyalty" ? "traders"
            : "items";
        var targets = string.Join(
            " or ",
            (condition.Target?.Values ?? []).Where(t => t.Length > 0).Select(t => ReferenceNames.Resolve(season, targetKind, t, installed))
        );
        var amount = condition.Value?.ToString(CultureInfo.InvariantCulture) ?? "unset";
        var comparison = condition.CompareMethod ?? ">=";
        var summary = kind switch
        {
            "Level" => $"Player level {comparison} {amount}",
            "Quest" =>
                $"{(targets.Length > 0 ? targets : "Choose a prerequisite quest")} · {string.Join(" or ", (condition.Status ?? []).Select(Status))}",
            "TraderLoyalty" => $"{targets} · loyalty {comparison} {amount}",
            "FindItem" or "HandoverItem" or "HasItem" or "LeaveItemAtLocation" =>
                $"{ObjectiveKind(kind)} · {comparison} {amount} · {(targets.Length > 0 ? targets : "Choose accepted items")}",
            "CounterCreator" =>
                $"Count raid events · {comparison} {amount} · {string.Join(", ", condition.Counter?.Conditions.Select(c => ObjectiveKind(c.ConditionType)) ?? [])}",
            _ => ObjectiveKind(kind),
        };
        if (condition.OnlyFoundInRaid == true)
        {
            summary += " · found in raid";
        }

        if (condition.OneSessionOnly == true)
        {
            summary += " · one raid";
        }

        if (condition.AvailableAfter > 0)
        {
            summary += $" · delay {condition.AvailableAfter} seconds";
        }

        return summary;
    }

    public static string Condition(SeasonDefinition season, StoryCondition condition, Func<string, string, string> installed, int depth = 0)
    {
        if (depth > 32)
        {
            return "Condition nesting is too deep";
        }

        var children = condition.Conditions.Select(c => Condition(season, c, installed, depth + 1));
        var target = ReferenceNames.Resolve(season, StoryAuthoring.ReferenceKind(condition, "Target") ?? "", condition.Target, installed);
        var summary = condition.Type switch
        {
            "All" => condition.Conditions.Count == 0 ? "Always" : "All of: " + string.Join("; ", children),
            "Any" => condition.Conditions.Count == 0 ? "Never (no alternatives)" : "Any of: " + string.Join("; ", children),
            "Not" => "Not: " + string.Join("; ", children),
            "QuestStatus" => $"{target} is {string.Join(" or ", condition.Status.Select(Status))}",
            "VariableValue" => $"{target} {condition.Operator} {condition.Value.ToString(CultureInfo.InvariantCulture)}",
            _ =>
                $"{StoryAuthoring.Friendly(condition.Type)} · {target} {condition.Operator} {condition.Value.ToString(CultureInfo.InvariantCulture)}",
        };
        return condition.InRaidOnly ? summary + " · only while in raid" : summary;
    }
}
