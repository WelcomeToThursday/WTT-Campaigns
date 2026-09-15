using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class MissionAuthoringChecks
{
    private static string Id() => SeasonRepository.NewId();

    internal static void Run(Action<bool, string> check)
    {
        var layout = MapEditorChecks.Example();
        var questId = Id();
        var conditionId = Id();
        var variableId = Id();
        var chapterId = Id();
        var quest = new NativeQuest
        {
            Id = questId,
            QuestName = "Route quest",
            Conditions = new()
            {
                AvailableForFinish =
                [
                    new NativeCondition
                    {
                        Id = conditionId,
                        ConditionType = "GlobalVariableValue",
                        Target = variableId,
                        Value = 1,
                        CompareMethod = ">=",
                    },
                ],
            },
        };
        var season = new SeasonDefinition
        {
            Id = Id(),
            BattlePassId = Id(),
            FormatVersion = 7,
            MapLayouts = [layout],
            Quests = [quest],
            Story = new()
            {
                Chapters = [new StoryChapter { Id = chapterId, Name = "Route" }],
                Quests = [new StoryQuest { QuestId = questId, ChapterId = chapterId }],
                Variables =
                [
                    new StoryVariable
                    {
                        Id = variableId,
                        Scope = StoryVariableScope.Profile,
                        InitialValue = 0,
                    },
                ],
            },
            Missions =
            [
                new()
                {
                    Id = Id(),
                    Name = "Route mission",
                    Briefing = "Follow the route and extract.",
                    LayoutId = layout.Id,
                    QuestId = questId,
                    CompletionConditionId = conditionId,
                },
            ],
        };
        var mission = season.Missions.Single();

        var missionIssues = SeasonValidator
            .Validate(season)
            .Issues.Where(issue => issue.Path.StartsWith("Missions/", StringComparison.Ordinal))
            .ToArray();
        check(missionIssues.Length == 0, "A mission links a walkthrough layout, story quest and profile completion condition");

        var before = JsonConvert.SerializeObject(season);
        var copy = SeasonRepository.Duplicate(season);
        var copiedMission = copy.Missions.Single();
        check(
            copiedMission.Id != mission.Id
                && copiedMission.LayoutId == copy.MapLayouts.Single().Id
                && copiedMission.QuestId == copy.Quests.Single().Id
                && copiedMission.CompletionConditionId == copy.Quests.Single().Conditions.AvailableForFinish.Single().Id,
            "Campaign duplication remaps mission and linked native identities"
        );
        check(JsonConvert.SerializeObject(season) == before, "Mission duplication preserves the source definition");

        var presentationHash = SeasonRepository.GameplayHash(season);
        mission.Name = "Renamed route";
        mission.Briefing = "A revised route briefing.";
        check(SeasonRepository.GameplayHash(season) == presentationHash, "Mission text changes preserve gameplay hash");
        var roundTrip = JObject.FromObject(JsonConvert.DeserializeObject<SeasonDefinition>(JsonConvert.SerializeObject(season))!);
        check(
            (string?)roundTrip["Missions"]?[0]?["LayoutId"] == mission.LayoutId
                && (string?)roundTrip["Missions"]?[0]?["CompletionConditionId"] == conditionId,
            "Mission content round trips through campaign JSON"
        );

        var ownedZone = new SeasonZone
        {
            Id = Id(),
            Name = "Mission zone",
            Location = layout.Location,
            Scene = "woods_main",
            LayoutId = layout.Id,
            Uses = ["InZone"],
        };
        var zoneObjective = new NativeCondition
        {
            Id = Id(),
            ConditionType = "InZone",
            ZoneIds = [ownedZone.Id],
        };
        quest.Conditions.AvailableForFinish.Add(zoneObjective);
        season.Zones.Add(ownedZone);
        var zonePublication = SeasonValidator.Validate(season);
        check(
            !zonePublication.Issues.Any(issue => issue.Path == "Zones/" + ownedZone.Id && issue.Severity == "error"),
            "Mission-linked layout zones are allowed for the matching mission layout"
        );

        var unrelatedQuest = new NativeQuest { Id = Id(), Conditions = new() };
        unrelatedQuest.Conditions.AvailableForFinish.Add(
            new NativeCondition { Id = Id(), ConditionType = "InZone", ZoneIds = [ownedZone.Id] }
        );
        season.Quests.Add(unrelatedQuest);
        var ordinaryPublication = SeasonValidator.Validate(season);
        check(
            ordinaryPublication.Issues.Any(issue => issue.Path == "Zones/" + ownedZone.Id && issue.Severity == "error"),
            "Ordinary quest references to mission-owned zones remain blocked"
        );
        season.Quests.Remove(unrelatedQuest);

        check(
            !ZoneLayoutRules.TryDeleteLayout(season, layout.Id, out var layoutError) && layoutError.Contains("Mission"),
            "A mission reference blocks layout deletion"
        );
        var questUses = QuestStoryFlow.DeleteQuest(season, quest);
        check(questUses.Any(path => path.Contains("Missions", StringComparison.Ordinal)), "A mission reference blocks quest deletion");
        check(
            StoryAuthoring.Uses(season, season.Story!.Variables.Single()).Any(path => path.Contains("Quests", StringComparison.Ordinal)),
            "The native completion condition blocks story variable deletion"
        );

        var old = SeasonCompiler.Copy(season);
        old.FormatVersion = 6;
        old.Missions.Clear();
        check(
            !SeasonValidator.Validate(old).Issues.Any(issue => issue.Message.Contains("Spatial content requires campaign format")),
            "Format 7 spatial support does not change older campaign validation"
        );
    }
}
