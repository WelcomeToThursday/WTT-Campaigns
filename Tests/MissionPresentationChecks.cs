using Newtonsoft.Json;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class MissionPresentationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var mission = new MissionDefinition
        {
            NotificationIcon = "quest:Elimination",
            CheckpointNotificationIcon = "quest:Discover",
            Objectives =
            [
                new()
                {
                    Id = "hold",
                    Name = "Secure the warehouse",
                    Type = MissionObjective.Defend,
                    ZoneId = "zone",
                    Seconds = 5,
                },
            ],
            Requirements = [new() { CheckpointId = "checkpoint", ObjectiveIds = ["hold"] }],
        };
        var layout = new MapLayout
        {
            Checkpoints = [new() { Id = "checkpoint", Name = "Warehouse checkpoint" }],
            Exit = new() { Id = "exit", Name = "South gate" },
        };
        var run = new MissionRun();
        var presentation = new MissionPresentation();
        presentation.Reset(run);
        check(presentation.Accept(mission, layout, run).Count == 0, "Pending mission objectives do not announce starts");
        check(
            MissionPresentation.Rows(mission, layout, run).Single().Progress == "Locked",
            "Checkpoint row respects objective requirements"
        );
        MissionLogic.Apply(mission, layout, run.Logic, new() { Kind = MissionSignals.Start });
        var notices = presentation.Accept(mission, layout, run);
        check(notices[0].Icon == "quest:Elimination", "Objective start inherits the server mission icon");
        mission.Objectives[0].NotificationIcon = "quest:Skill";
        check(
            notices.Count == 1 && notices[0].Title == "Secure the warehouse" && notices[0].Status == "Started",
            "Accepted mission start announces authored objective"
        );
        check(presentation.Accept(mission, layout, run).Count == 0, "Repeated acknowledged snapshots do not duplicate story banners");
        var rows = MissionPresentation.Rows(mission, layout, run);
        check(
            rows.Count == 2 && rows[0].Progress == "0/5s" && rows[1].Text == "Warehouse checkpoint",
            "Native rows show objective progress and named checkpoint"
        );
        MissionLogic.Apply(
            mission,
            layout,
            run.Logic,
            new()
            {
                Kind = MissionSignals.Sample,
                TargetId = "zone",
                PlayerInside = true,
            }
        );
        MissionLogic.Apply(mission, layout, run.Logic, new() { Kind = MissionSignals.Tick, Time = 5 });
        notices = presentation.Accept(mission, layout, run);
        check(notices[0].Icon == "quest:Skill", "Objective completion uses its explicit icon override");
        check(
            notices.Count == 1 && notices[0].Description == "Objective completed" && notices[0].Status == "Complete",
            "Evaluator Completed status selects story completion banner"
        );
        rows = MissionPresentation.Rows(mission, layout, run);
        check(rows.Count == 1 && rows[0].Progress == "Reach", "Completed objectives leave the route prompt unlocked");
        run.NextCheckpointIndex = 1;
        notices = presentation.Accept(mission, layout, run);
        check(notices[0].Icon == "quest:Discover", "Checkpoint notification uses the mission checkpoint icon");
        check(
            notices.Count == 1 && notices[0].Title == "Warehouse checkpoint" && notices[0].Description == "Checkpoint reached",
            "Accepted checkpoint announces its authored name"
        );
        check(
            MissionPresentation.Rows(mission, layout, run).Single().Text == "South gate",
            "Completed route points to authored mission exit"
        );
        check(presentation.Accept(mission, layout, run).Count == 0, "Replayed checkpoint receipt stays silent");

        // A retry establishes a baseline without replaying checkpoints or objectives.
        run.Logic.Objectives["hold"].Status = "Active";
        run.NextCheckpointIndex = 0;
        presentation.Reset(run);
        check(presentation.Accept(mission, layout, run).Count == 0, "Checkpoint restore does not replay objective starts");
        run.Logic.Objectives["hold"].Status = "Completed";
        run.NextCheckpointIndex = 1;
        notices = presentation.Accept(mission, layout, run);
        check(notices.Count == 2, "Re-earned objective and checkpoint completion announce after retry");
        run.Logic.Objectives["hold"].Status = "Failed";
        check(presentation.Accept(mission, layout, run).Single().Status == "Failed", "Objective failure uses story failure variant");

        // One accepted batch can activate and finish an objective; do not invent a late start.
        presentation.Reset(new MissionRun());
        run.NextCheckpointIndex = 0;
        run.Logic.Objectives["hold"].Status = "Completed";
        notices = presentation.Accept(mission, layout, run);
        check(notices.Count == 1 && notices[0].Status == "Complete", "Batched completion avoids an obsolete started notification");
        check(
            MissionPresentation.Rows(new MissionDefinition(), new MapLayout { Exit = layout.Exit }, new MissionRun()).Single().Text
                == "South gate",
            "Zero-checkpoint missions immediately show the authored exit"
        );
        run.ExitReached = true;
        run.NextCheckpointIndex = 1;
        check(MissionPresentation.Rows(mission, layout, run).Single().Progress == "Reached", "Accepted mission exit shows reached state");
        check(
            !JsonConvert.SerializeObject(new MissionDefinition()).Contains("NotificationIcon")
                && !JsonConvert.SerializeObject(new MissionObjective()).Contains("NotificationIcon"),
            "Unconfigured icons preserve legacy serialized mission identity"
        );
        var season = new SeasonDefinition { Missions = [mission] };
        var identity = SeasonCompiler.GameplayIdentity(season);
        mission.Objectives[0].NotificationIcon = "1234567890abcdef12345678";
        mission.NotificationIcon = "aaaaaaaaaaaaaaaaaaaaaaaa";
        mission.CheckpointNotificationIcon = "bbbbbbbbbbbbbbbbbbbbbbbb";
        var copy = SeasonCompiler.Copy(season);
        check(
            copy.Missions[0].Objectives[0].NotificationIcon == mission.Objectives[0].NotificationIcon,
            "Saved mission descriptors retain custom notification artwork"
        );
        check(SeasonCompiler.GameplayIdentity(season) == identity, "Notification artwork changes are presentation-only");
        check(
            SeasonCompiler.Assets(copy).Contains(mission.Objectives[0].NotificationIcon)
                && SeasonCompiler.Assets(copy).Contains(mission.NotificationIcon)
                && SeasonCompiler.Assets(copy).Contains(mission.CheckpointNotificationIcon),
            "Mission export includes all custom notification artwork"
        );
        mission.NotificationIcon = "quest:Unknown";
        check(MissionLogicRules.DraftErrors(mission).Count > 0, "Unknown built-in icons are rejected before publishing");
        check(MissionNotificationIcons.Resolve("") == "quest:Completion", "Older missions receive a visible native default icon");
        foreach (var icon in MissionNotificationIcons.Choices.Keys)
            check(MissionNotificationIcons.IsValid(icon), "Supported native quest icon accepted: " + icon);
    }
}
