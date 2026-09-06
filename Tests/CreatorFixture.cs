using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Seasons;

namespace SeasonalPerks.Tests;

internal static class CreatorFixture
{
    public static void Prepare(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (!path.EndsWith(Path.Combine("Testing", "Server", "user", "mods", "SeasonalPerks"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Creator fixtures require the isolated Testing/Server mod directory.");
        }

        var store = new SeasonRepository(path);
        var draft = store.Create(false);
        var s = draft.Definition;
        s.Name = "Creator acceptance season";
        s.Rules.StartingPoints = 1;
        var perk = SeasonRepository.NewId();
        s.Perks.Personal.Add(
            new()
            {
                Id = perk,
                Type = "personal",
                Points = -1,
                ImageUrl = s.UniversalImage,
                Effects = [new() { EffectId = "energy_drain_multiplicator", Multiplier = .8 }],
            }
        );
        s.Locales["en"][perk + " name"] = "Enduring";
        s.Locales["en"][perk + " description"] = "Energy drains 20% slower.";
        var document = s.Documents[0];
        document.Name = "Research notes";
        s.Items[0].Name = document.Name;
        var crate = SeasonRepository.NewId();
        s.Items.Add(
            new()
            {
                Id = crate,
                CloneFrom = "6a3567f687d90a0deb066c1b",
                Name = "Research supply crate",
                StackMax = 1,
            }
        );
        s.Crates.Add(
            new()
            {
                ItemId = crate,
                Pool = new() { ["5449016a4bdc2d6f028b456f"] = 1 },
                RewardCount = 2,
            }
        );
        s.ExchangeCrate = crate;
        s.Starting.Usec.Items.Add(new() { Template = document.ItemId, Count = 8 });
        s.Starting.Usec.Skills["Strength"] = 2;
        var quest = SeasonRepository.NewId();
        var objective = SeasonRepository.NewId();
        var level = SeasonRepository.NewId();
        s.Quests.Add(
            new JObject
            {
                ["_id"] = quest,
                ["QuestName"] = "Research delivery",
                ["name"] = quest + " name",
                ["description"] = quest + " description",
                ["traderId"] = "54cb50c76803fa8b248b4571",
                ["location"] = "any",
                ["image"] = "/files/quest/icon/596b36c586f77450d6045ad2.jpg",
                ["type"] = "PickUp",
                ["side"] = "Pmc",
                ["canShowNotificationsInGame"] = true,
                ["restartable"] = false,
                ["conditions"] = new JObject
                {
                    ["AvailableForStart"] = new JArray(
                        new JObject
                        {
                            ["id"] = level,
                            ["conditionType"] = "Level",
                            ["dynamicLocale"] = false,
                            ["value"] = 1,
                            ["compareMethod"] = ">=",
                            ["visibilityConditions"] = new JArray(),
                        }
                    ),
                    ["AvailableForFinish"] = new JArray(
                        new JObject
                        {
                            ["id"] = objective,
                            ["conditionType"] = "HandoverItem",
                            ["dynamicLocale"] = false,
                            ["target"] = new JArray(document.ItemId),
                            ["value"] = 1,
                            ["onlyFoundInRaid"] = false,
                            ["minDurability"] = 0,
                            ["maxDurability"] = 100,
                            ["visibilityConditions"] = new JArray(),
                        }
                    ),
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
                    ["en"] = new JObject
                    {
                        [quest + " name"] = "Research delivery",
                        [quest + " description"] = "Hand over a research note.",
                        [objective] = "Hand over a research note",
                    },
                },
            }
        );
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
            s.Quests[0][field] = quest + " " + field;
            s.Quests[0]["localization"]!["en"]![quest + " " + field] = "Research delivery";
        }
        var followup = SeasonRepository.NewId();
        var followupObjective = SeasonRepository.NewId();
        var next = JObject.Parse(s.Quests[0].ToString().Replace(quest, followup).Replace(objective, followupObjective));
        next["conditions"]!["AvailableForStart"] = new JArray(
            new JObject
            {
                ["id"] = SeasonRepository.NewId(),
                ["conditionType"] = "Quest",
                ["target"] = quest,
                ["status"] = new JArray(4),
                ["value"] = 1,
                ["dynamicLocale"] = false,
                ["visibilityConditions"] = new JArray(),
            }
        );
        next["QuestName"] = "Research follow-up";
        next["localization"]!["en"]![followup + " name"] = "Research follow-up";
        s.Quests.Add(next);
        s.Locales["fr"] = new() { [document.Id + " name"] = "Notes de recherche", [s.Id + " name"] = "Saison de recherche" };
        SeasonReward Reward(string name, string template, int x)
        {
            var item = SeasonRepository.NewId();
            return new()
            {
                Id = SeasonRepository.NewId(),
                Name = name,
                X = x,
                Image = s.UniversalImage,
                BigImage = s.UniversalImage,
                Grants = new JArray(
                    new JObject
                    {
                        ["id"] = SeasonRepository.NewId(),
                        ["type"] = "Item",
                        ["target"] = item,
                        ["items"] = new JArray(
                            new JObject
                            {
                                ["_id"] = item,
                                ["_tpl"] = template,
                                ["upd"] = new JObject { ["StackObjectsCount"] = 1 },
                            }
                        ),
                    }
                ),
            };
        }
        var gated = Reward("Research reward", "5449016a4bdc2d6f028b456f", 0);
        gated.Conditions.Add(new JObject { ["conditionType"] = "Quest", ["target"] = followup });
        gated.Costs.Add(new() { DocumentId = document.Id, Count = 1 });
        var supply = Reward("Supply crate", crate, 1);
        s.Pages[0].Rewards = [gated, supply];
        draft = store.Save(draft);
        var validation = SeasonValidator.Validate(s);
        if (!validation.CanPublish)
        {
            throw new InvalidDataException(JsonConvert.SerializeObject(validation));
        }

        var key = store.Publish(draft, validation);
        store.Queue(key);
        var fixture = new
        {
            SeasonId = s.Id,
            Pack = key,
            PerkId = perk,
            DocumentId = document.Id,
            DocumentTemplate = document.ItemId,
            QuestId = quest,
            ObjectiveId = objective,
            FollowupQuest = followup,
            FollowupObjective = followupObjective,
            GatedReward = gated.Id,
            CrateReward = supply.Id,
            Crate = crate,
        };
        File.WriteAllText(
            Path.Combine(path, "creator", "acceptance-fixture.json"),
            JsonConvert.SerializeObject(fixture, Formatting.Indented)
        );
        Console.WriteLine("Prepared creator acceptance fixture: " + key);
    }
}
