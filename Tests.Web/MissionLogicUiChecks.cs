using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

internal static class MissionLogicUiChecks
{
    public static async Task Run(IServiceProvider services, Action<bool, string> check)
    {
        var layout = new MapLayout
        {
            Encounters =
            [
                new()
                {
                    Id = "enc",
                    Name = "Encounter",
                    Waves =
                    [
                        new()
                        {
                            Id = "wave",
                            Roster =
                            [
                                new() { Id = "single", Count = 1 },
                                new() { Id = "single2", Count = 1 },
                                new() { Id = "group", Count = 2 },
                            ],
                        },
                    ],
                },
            ],
        };
        var objective = new MissionObjective
        {
            Id = "goal",
            TargetKind = "Roster",
            TargetIds = ["single", "group"],
        };
        var mission = new MissionDefinition { Objectives = [objective] };
        await using var renderer = new EditorRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.Mount(new Host(mission, layout));
            MissionChoice Choice(string label) => renderer.Components<MissionChoice>().Single(c => c.Component.Label == label).Component;
            check(
                Choice("Notification icon").Choices.Count() == 15,
                "Objective picker offers all fourteen Tarkov quest icons and custom PNGs"
            );
            await Choice("Default objective icon").ValueChanged.InvokeAsync("quest:Elimination");
            await Choice("Checkpoint icon").ValueChanged.InvokeAsync("quest:Exploration");
            await Choice("Notification icon").ValueChanged.InvokeAsync("quest:WeaponAssembly");
            check(
                mission.NotificationIcon == "quest:Elimination"
                    && mission.CheckpointNotificationIcon == "quest:Exploration"
                    && objective.NotificationIcon == "quest:WeaponAssembly",
                "Creator writes selected native icons onto mission and objective records"
            );
            await Choice("Notification icon").ValueChanged.InvokeAsync("");
            check(objective.NotificationIcon == "", "Objective can return to the mission default");
            await Choice("Notification icon").ValueChanged.InvokeAsync("Custom");
            var artwork = renderer.Components<AssetPicker>().Single().Component;
            await artwork.ValueChanged.InvokeAsync("1234567890abcdef12345678");
            check(objective.NotificationIcon == "1234567890abcdef12345678", "Uploaded artwork selection is saved on the objective");
            await Choice("Notification icon").ValueChanged.InvokeAsync("quest:Elimination");
            check(!renderer.Components<AssetPicker>().Any(), "Choosing a built-in icon removes the custom artwork selector");
            check(!Choice("Goal").AllowEmpty, "Goal cannot be cleared to an unsupported value");
            await Choice("Goal").ValueChanged.InvokeAsync(MissionObjective.Target);
            check(objective.TargetIds.SequenceEqual(new[] { "single" }), "Individual goal retains only compatible existing actors");
            check(
                Choice("Target group").Choices.Select(c => c.Id).SequenceEqual(new[] { "Roster" }),
                "Individual goals offer only roster targets"
            );
            check(
                Choice("Actor").Choices.Select(c => c.Id).SequenceEqual(new[] { "single", "single2" }),
                "Actor picker excludes multiple-bot rosters"
            );
            await Choice("Actor").ValueChanged.InvokeAsync("single2");
            check(objective.TargetIds.SequenceEqual(new[] { "single2" }), "Actor selection replaces the previous actor");
            await Choice("Actor").ValueChanged.InvokeAsync("group");
            check(objective.TargetIds.SequenceEqual(new[] { "single2" }), "Invalid actor cannot be selected");
            await Choice("Goal").ValueChanged.InvokeAsync(MissionObjective.Protect);
            check(Choice("Actor").Choices.Count() == 2, "Protect uses the same single-actor restriction");
            objective.UntilEventId = "old-event";
            await Choice("Goal").ValueChanged.InvokeAsync(MissionObjective.Survive);
            check(
                objective.TargetKind == "Encounter" && objective.TargetIds.Count == 0,
                "Survive changes target kind and clears incompatible actor IDs"
            );
            check(objective.UntilEventId.Length == 0, "Leaving Protect clears its incompatible endpoint");
            check(Choice("Target group").Choices.Select(c => c.Id).SequenceEqual(new[] { "Encounter" }), "Survive offers only encounters");
            await Choice("Target group").ValueChanged.InvokeAsync("Squad");
            check(objective.TargetKind == "Encounter", "Unsupported target kind is rejected");
            await Choice("Goal").ValueChanged.InvokeAsync(MissionObjective.Eliminate);
            check(
                Choice("Target group").Choices.Count() == 3 && !renderer.Components<MissionChoice>().Any(c => c.Component.Label == "Actor"),
                "Group goals retain all group kinds and multiple selection"
            );
            await Choice("Target group").ValueChanged.InvokeAsync("Roster");
            objective.TargetIds = ["single", "single2"];
            await Choice("Goal").ValueChanged.InvokeAsync(MissionObjective.Target);
            check(objective.TargetIds.Count == 0, "Switching multiple actors to an individual goal requires a deliberate choice");
            check(
                mission.Objectives.Count == 1 && layout.Encounters[0].Waves[0].Roster[2].Count == 2,
                "Choice filtering preserves authored objectives and roster counts"
            );
        });
    }

    private sealed class Host(MissionDefinition mission, MapLayout layout) : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MissionLogicFields>(0);
            builder.AddAttribute(1, "Mission", mission);
            builder.AddAttribute(2, "Layout", layout);
            builder.CloseComponent();
        }
    }
}
