using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Server.Web.Authoring;

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
        return nested ? CounterFilters.Concat(new[] { "Salvage" })
            : story ? WTT.Campaigns.Shared.Story.StoryQuestCompatibility.ConditionTypes.Except(CounterFilters).Order()
            :
            [
                "Level",
                "Quest",
                "TraderLoyalty",
                "FindItem",
                "HandoverItem",
                "VisitPlace",
                "LeaveItemAtLocation",
                "Salvage",
                "CounterCreator",
            ];
    }

    public static NativeCondition Condition(string kind)
    {
        var condition = new NativeCondition
        {
            Id = SeasonRepository.NewId(),
            ConditionType = kind,
            Index = 0,
            ParentId = "",
            DynamicLocale = false,
            Value = 1,
            CompareMethod = ">=",
            Target = kind == "Level" ? null : new StringTargets(""),
        };
        if (kind is "FindItem" or "HasItem" or "HandoverItem" or "LeaveItemAtLocation")
        {
            condition.Target = new StringTargets(new[] { "" });
            condition.OnlyFoundInRaid = false;
            condition.MinDurability = 0;
            condition.MaxDurability = 100;
        }
        if (kind == "Quest")
        {
            condition.Status = ["4"];
            condition.AvailableAfter = 0;
        }
        if (kind == "CounterCreator")
        {
            condition.Target = null;
            condition.Counter = new() { Id = SeasonRepository.NewId() };
            condition.OneSessionOnly = false;
        }
        if (kind is "VisitPlace" or "LeaveItemAtLocation" or "InZone" or "LaunchFlare")
        {
            condition.ZoneId = "";
        }

        if (kind == "InZone")
        {
            condition.ZoneId = null;
            condition.ZoneIds = [];
        }
        if (kind == "VisitPlace")
        {
            condition.ZoneId = null;
        }

        if (kind == "Salvage")
        {
            condition.Target = null;
            condition.ZoneId = "";
        }
        if (kind is "LeaveItemAtLocation" or "Salvage")
        {
            condition.PlantTime = 10;
        }

        if (kind is "Kills" or "Shots")
        {
            condition.Target = "Any";
            condition.BodyPart = [];
            condition.Weapon = [];
            if (kind == "Kills")
            {
                condition.Distance = new() { CompareMethod = ">=", Value = 0 };
            }
        }
        if (kind is "Location" or "ExitStatus" or "ExitName" or "Equipment" or "UseItem")
        {
            condition.Target = new StringTargets(Array.Empty<string>());
        }

        return condition;
    }

    public static NativeQuest Create()
    {
        var id = SeasonRepository.NewId();
        var quest = new NativeQuest
        {
            Id = id,
            QuestName = "New quest",
            Name = id + " name",
            Description = id + " description",
            TraderId = "54cb50c76803fa8b248b4571",
            Location = "any",
            Image = "/files/quest/icon/596b36c586f77450d6045ad2.jpg",
            Type = "PickUp",
            Side = "Pmc",
            CanShowNotificationsInGame = true,
            Restartable = false,
            InstantComplete = false,
            Rewards = new()
            {
                ["Started"] = new(),
                ["Success"] = new(),
                ["Fail"] = new(),
            },
            Localization = new()
            {
                ["en"] = new() { [id + " name"] = "New quest", [id + " description"] = "Complete the objectives." },
            },
        };
        foreach (
            var field in new[]
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
            QuestText(quest, field, field == "successMessageText" ? "Well done." : "I have a task for you.");
        }

        AddCondition(quest, "AvailableForStart", "Level");
        AddCondition(quest, "AvailableForFinish", "HandoverItem");
        return quest;
    }

    public static string? LocaleKey(NativeQuest quest, string field)
    {
        return field switch
        {
            "name" => quest.Name,
            "description" => quest.Description,
            "startedMessageText" => quest.StartedMessageText,
            "successMessageText" => quest.SuccessMessageText,
            "failMessageText" => quest.FailMessageText,
            "acceptPlayerMessage" => quest.AcceptPlayerMessage,
            "completePlayerMessage" => quest.CompletePlayerMessage,
            "declinePlayerMessage" => quest.DeclinePlayerMessage,
            _ => throw new ArgumentException("Unknown quest text: " + field),
        };
    }

    public static string QuestLocale(NativeQuest quest, string field)
    {
        return quest.Text(LocaleKey(quest, field) ?? "");
    }

    public static string QuestName(NativeQuest quest)
    {
        return QuestLocale(quest, "name") is { Length: > 0 } name ? name : quest.QuestName ?? quest.Id;
    }

    public static void QuestText(NativeQuest quest, string field, string value)
    {
        var key = LocaleKey(quest, field) ?? quest.Id + " " + field;
        switch (field)
        {
            case "name":
                quest.Name = key;
                quest.QuestName = value;
                break;
            case "description":
                quest.Description = key;
                break;
            case "startedMessageText":
                quest.StartedMessageText = key;
                break;
            case "successMessageText":
                quest.SuccessMessageText = key;
                break;
            case "failMessageText":
                quest.FailMessageText = key;
                break;
            case "acceptPlayerMessage":
                quest.AcceptPlayerMessage = key;
                break;
            case "completePlayerMessage":
                quest.CompletePlayerMessage = key;
                break;
            case "declinePlayerMessage":
                quest.DeclinePlayerMessage = key;
                break;
        }
        quest.English()[key] = value;
    }

    public static void AddCondition(NativeQuest quest, string stage, string kind)
    {
        if (kind.Length == 0)
        {
            return;
        }

        var condition = Condition(kind);
        condition.Index = quest.Conditions.Stage(stage).Count;
        quest.Conditions.Stage(stage).Add(condition);
        quest.English()[condition.Id] = kind == "HandoverItem" ? "Hand over the required items" : "Complete the requirement";
    }

    public static NativeReward Reward(string kind)
    {
        var reward = new NativeReward
        {
            Id = SeasonRepository.NewId(),
            Type = kind,
            Target = "",
            Value = kind == "Experience" ? 100 : 1,
        };
        if (kind == "Item")
        {
            reward.Target = SeasonRepository.NewId();
            reward.Items.Add(
                new()
                {
                    Id = reward.Target,
                    Template = "",
                    Upd = new() { StackObjectsCount = 1 },
                }
            );
        }
        return reward;
    }

    public static void AddQuestReward(NativeQuest quest, string rewardKind, string stage = "Success")
    {
        if (!quest.Rewards.TryGetValue(stage, out var rewards))
        {
            quest.Rewards[stage] = rewards = new();
        }

        rewards.Add(Reward(rewardKind));
    }
}
