using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;

namespace SeasonalPerks.Server.Web.Authoring;

public static class NativeQuestAuthoring
{
    private static readonly string[] CounterFilters =
    [
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
    ];

    public static IEnumerable<string> ConditionKinds(bool story, bool nested)
    {
        return nested ? CounterFilters
            : story ? SeasonalPerks.Shared.Story.StoryQuestCompatibility.ConditionTypes.Except(CounterFilters).Order()
            : ["Level", "Quest", "TraderLoyalty", "FindItem", "HandoverItem"];
    }

    public static JObject Condition(string kind)
    {
        var condition = new JObject
        {
            ["id"] = SeasonRepository.NewId(),
            ["conditionType"] = kind,
            ["index"] = 0,
            ["parentId"] = "",
            ["dynamicLocale"] = false,
            ["visibilityConditions"] = new JArray(),
            ["value"] = 1,
            ["compareMethod"] = ">=",
        };
        if (kind != "Level")
        {
            condition["target"] = "";
        }

        if (kind is "FindItem" or "HasItem" or "HandoverItem" or "LeaveItemAtLocation")
        {
            condition["target"] = new JArray("");
            condition["onlyFoundInRaid"] = false;
            condition["minDurability"] = 0;
            condition["maxDurability"] = 100;
        }

        if (kind == "Quest")
        {
            condition["status"] = new JArray(4);
            condition["availableAfter"] = 0;
        }

        if (kind == "CounterCreator")
        {
            condition.Remove("target");
            condition["counter"] = new JObject { ["id"] = SeasonRepository.NewId(), ["conditions"] = new JArray() };
            condition["oneSessionOnly"] = false;
        }

        if (kind is "VisitPlace" or "LeaveItemAtLocation" or "InZone" or "LaunchFlare")
        {
            condition["zoneId"] = "";
        }

        if (kind == "LeaveItemAtLocation")
        {
            condition["plantTime"] = 10;
        }

        if (kind == "Kills")
        {
            condition["target"] = "Any";
            condition["bodyPart"] = new JArray();
            condition["weapon"] = new JArray();
            condition["distance"] = new JObject { ["compareMethod"] = ">=", ["value"] = 0 };
        }

        if (kind == "Shots")
        {
            condition["target"] = "Any";
            condition["bodyPart"] = new JArray();
            condition["weapon"] = new JArray();
        }

        if (kind is "Location" or "ExitStatus" or "ExitName" or "Equipment" or "UseItem")
        {
            condition["target"] = new JArray();
        }

        return condition;
    }

    public static JObject Create()
    {
        var id = SeasonRepository.NewId();
        var quest = new JObject
        {
            ["_id"] = id,
            ["QuestName"] = "New quest",
            ["name"] = id + " name",
            ["description"] = id + " description",
            ["traderId"] = "54cb50c76803fa8b248b4571",
            ["location"] = "any",
            ["image"] = "/files/quest/icon/596b36c586f77450d6045ad2.jpg",
            ["type"] = "PickUp",
            ["side"] = "Pmc",
            ["canShowNotificationsInGame"] = true,
            ["restartable"] = false,
            ["instantComplete"] = false,
            ["conditions"] = new JObject
            {
                ["AvailableForStart"] = new JArray(),
                ["AvailableForFinish"] = new JArray(),
                ["Fail"] = new JArray(),
            },
            ["rewards"] = new JObject
            {
                ["Started"] = new JArray(),
                ["Success"] = new JArray(),
                ["Fail"] = new JArray(),
            },
            ["localization"] = new JObject
            {
                ["en"] = new JObject { [id + " name"] = "New quest", [id + " description"] = "Complete the objectives." },
            },
        };
        foreach (
            var key in new[]
            {
                "startedMessageText",
                "successMessageText",
                "failMessageText",
                "acceptPlayerMessage",
                "completePlayerMessage",
                "declinePlayerMessage",
            }
        )
        {
            quest[key] = id + " " + key;
            quest["localization"]!["en"]![id + " " + key] = key == "successMessageText" ? "Well done." : "I have a task for you.";
        }

        AddCondition(quest, "AvailableForStart", "Level");
        AddCondition(quest, "AvailableForFinish", "HandoverItem");
        return quest;
    }

    public static string QuestLocale(JObject quest, string field)
    {
        return (string?)quest["localization"]?["en"]?[(string?)quest[field] ?? ""] ?? "";
    }

    public static string QuestName(JObject quest)
    {
        return QuestLocale(quest, "name") is { Length: > 0 } name ? name : (string?)quest["QuestName"] ?? (string)quest["_id"]!;
    }

    public static void QuestText(JObject quest, string field, string value)
    {
        var key = (string?)quest[field] ?? (string)quest["_id"]! + " " + field;
        quest[field] = key;
        quest["localization"] ??= new JObject();
        quest["localization"]!["en"] ??= new JObject();
        quest["localization"]!["en"]![key] = value;
        if (field == "name")
        {
            quest["QuestName"] = value;
        }
    }

    public static void AddCondition(JObject quest, string stage, string kind)
    {
        if (kind.Length == 0)
        {
            return;
        }

        var condition = Condition(kind);
        var id = (string)condition["id"]!;
        condition["index"] = ((JArray)quest["conditions"]![stage]!).Count;
        ((JArray)quest["conditions"]![stage]!).Add(condition);
        quest["localization"]!["en"]![id] = kind == "HandoverItem" ? "Hand over the required items" : "Complete the requirement";
    }

    public static void AddQuestReward(JObject quest, string rewardKind, string stage = "Success")
    {
        if (rewardKind != "Item")
        {
            ((JArray)quest["rewards"]![stage]!).Add(
                new JObject
                {
                    ["id"] = SeasonRepository.NewId(),
                    ["type"] = rewardKind,
                    ["target"] = "",
                    ["value"] = rewardKind == "Experience" ? 100 : 1,
                }
            );
            return;
        }

        var id = SeasonRepository.NewId();
        ((JArray)quest["rewards"]![stage]!).Add(
            new JObject
            {
                ["id"] = SeasonRepository.NewId(),
                ["type"] = "Item",
                ["target"] = id,
                ["value"] = 1,
                ["items"] = new JArray(
                    new JObject
                    {
                        ["_id"] = id,
                        ["_tpl"] = "",
                        ["upd"] = new JObject { ["StackObjectsCount"] = 1 },
                    }
                ),
            }
        );
    }
}
