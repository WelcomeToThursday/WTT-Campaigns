using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class BuiltInCopyChecks
{
    internal static void Run(SeasonDefinition blank, SeasonDefinition copy, Action<bool, string> check)
    {
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        check(
            copy.QuestLoot.Count == 4 && copy.QuestLoot.All(l => copy.Quests.Any(q => q.Id == l.QuestId)),
            "Copied bot loot follows copied quest identities"
        );
        check(
            copy.Zones.Where(z => z.Uses.Contains("Salvage") || z.Uses.Contains("LeaveItemAtLocation"))
                .All(z => copy.Quests.Any(q => q.Id == z.RequiredQuestId)),
            "Recovery interactions follow the copied quest and cannot be collected before accepting it"
        );
        check(
            copy.Zones.Where(z => z.Uses.Contains("Salvage")).All(z => z.Salvage.Recovery),
            "Recovery uses repeatable native inventory transactions instead of permanent salvage counters"
        );
        var craft = copy.Crafts.Single();
        var nativeCraft = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Hideout.HideoutProduction>(JsonConvert.SerializeObject(craft))!;
        check(
            nativeCraft.Requirements.Any(r => r.QuestId?.ToString() == craft.Requirements.Single(r => r.Type == "QuestComplete").QuestId)
                && copy.Items.Any(i => i.Id == nativeCraft.EndProduct.ToString()),
            "Native recipe preserves the copied unlock and crafted item"
        );
        check(
            copy.TraderOffers.All(o => copy.Quests.Any(q => q.Id == o.UnlockQuestId && q.AllRewards().Any(r => r.Target == o.Id))),
            "Copied rifle offer retains its copied quest unlock"
        );
        check(
            copy.Quests.SelectMany(q => q.AllRewards()).All(r => r.AvailableInGameEditions == null || r.AvailableInGameEditions.Count == 0),
            "Captured edition restrictions cannot suppress SPT quest rewards"
        );
        check(
            copy.AllRewards.Any(r => !r.Enabled && r.Requirements.Any(t => t.Contains("unreleased"))),
            "The unreleased Historical Perspectives reward stays explicitly unavailable"
        );
        check(
            copy.Quests.All(q => q.Conditions.AvailableForFinish.All(c => c.IsNecessary != false)),
            "Captured counters cannot make scouting or combat quests completable without their objectives"
        );
        check(
            copy.Pages.Skip(1).Select((p, i) => p.PreviousRequirement <= copy.Pages[i].Rewards.Count(r => r.Enabled)).All(x => x),
            "Unavailable cosmetics cannot block subsequent reward pages"
        );
        foreach (var quest in copy.Quests.Where(q => q.SeasonalEnabled != false))
        {
            check(
                json.Deserialize<Quest>(JsonConvert.SerializeObject(quest)) != null,
                "Playable copied quest matches SPT's native contract"
            );
            foreach (var reward in quest.AllRewards().Where(r => r.Type == "Item"))
            {
                var result = new SeasonValidationResult();
                SeasonValidator.ItemTree(reward.Items, "Reward", result);
                check(
                    result.CanPublish && reward.Items.Any(i => i.Id == reward.Target),
                    "Copied reward retains a valid native target tree"
                );
            }
        }

        var season = SeasonCompiler.Copy(blank);
        NativeQuest Quest() =>
            new()
            {
                Id = SeasonRepository.NewId(),
                Localization = new() { ["en"] = new() },
            };
        NativeCondition Requires(NativeQuest target, string status = "4") =>
            new()
            {
                Id = SeasonRepository.NewId(),
                ConditionType = "Quest",
                Target = target.Id,
                Status = [status],
            };
        var first = Quest();
        var second = Quest();
        season.Quests = [first, second];
        second.Conditions.AvailableForStart.Add(Requires(first));
        first.Conditions.Fail.Add(Requires(second));
        check(
            SeasonValidator.Validate(season).CanPublish,
            "Quest-state gates need no quantity and fail conditions do not create prerequisite cycles"
        );
        first.Conditions.AvailableForStart.Add(Requires(second));
        check(
            SeasonValidator.Validate(season).Issues.Any(i => i.Message == "Quest dependency cycle."),
            "Real completion prerequisite cycle remains invalid"
        );
        first.Conditions.AvailableForStart.Clear();
        first.Conditions.AvailableForFinish.Add(Requires(second));
        second.Conditions.AvailableForStart[0].Status = ["2"];
        check(SeasonValidator.Validate(season).CanPublish, "A delivery subquest may start during its parent and finish before the parent");
        first.Conditions.AvailableForFinish.Clear();
        second.Conditions.AvailableForStart[0].Status = ["not-a-state"];
        check(!SeasonValidator.Validate(season).CanPublish, "Unknown quest state remains invalid");
        second.Conditions.AvailableForStart.Clear();
        second.Conditions.AvailableForFinish.Add(
            new()
            {
                Id = SeasonRepository.NewId(),
                ConditionType = "FindItem",
                Target = new(new[] { "5449016a4bdc2d6f028b456f" }),
                Value = 0,
            }
        );
        check(!SeasonValidator.Validate(season).CanPublish, "Item objectives still require positive quantities");
        second.Conditions.AvailableForFinish.Clear();
        first.SeasonalEnabled = false;
        first.Conditions.AvailableForStart.Add(Requires(first));
        second.Conditions.AvailableForStart.Add(Requires(first));
        check(
            !SeasonValidator.Validate(season).Issues.Any(i => i.Message == "Quest dependency cycle."),
            "Disabled quest contents are not traversed for cycles"
        );
        first.SeasonalEnabled = true;
        first.Conditions.AvailableForStart.Clear();
        first.Rewards["Success"] = [new() { Type = "UnknownReward" }];
        var authoredCopy = SeasonRepository.Duplicate(season);
        check(
            authoredCopy.Quests[0].SeasonalEnabled != false && !SeasonValidator.Validate(authoredCopy).CanPublish,
            "Duplicating an authored campaign preserves errors for correction instead of disabling content"
        );
    }
}
