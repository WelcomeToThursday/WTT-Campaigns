using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EditorContentChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static string Id(int i) => i.ToString("x24");
        static bool Reject(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
        var level = new MapLayout
        {
            Id = Id(1),
            Location = "woods",
            ApplyInNormalRaids = false,
        };
        var pack = new SeasonDefinition
        {
            Id = Id(10),
            MapLayouts = new() { level },
        };
        check(EditorContentRules.Mode(null) == EditorContentMode.None, "No content has a neutral editor title");
        check(EditorContentRules.Mode(pack, level.Id) == EditorContentMode.Level, "Disabled ordinary layouts use Level Editor");
        check(
            EditorContentRules.AvailableModes(pack).SequenceEqual(new[] { EditorContentMode.Level }),
            "Ordinary content appears only under Levels"
        );
        check(
            EditorContentRules.Includes(new EditorDraftChoice { Mode = EditorContentMode.Mission }, EditorContentMode.Mission),
            "Older picker responses retain their single content mode"
        );
        level.ApplyInNormalRaids = true;
        check(EditorContentRules.Mode(pack, level.Id) == EditorContentMode.Level, "Enablement does not change editor mode");
        foreach (var action in new[] { "MapStart", "MapCheckpoint", "AiObserve", "AiPlaytest", "TestCheckpoints" })
        {
            check(!EditorContentRules.ActionAllowed(EditorContentMode.Level, action), "Level rejects mission action " + action);
            check(EditorContentRules.ActionAllowed(EditorContentMode.Mission, action), "Mission allows " + action);
        }
        foreach (var action in new[] { "MapExit", "MapBarrier", "MapDoor", "AddBox", "AddMinefield", "Capture", "MapWalk" })
            check(EditorContentRules.ActionAllowed(EditorContentMode.Level, action), "Level retains " + action);
        check(!EditorContentRules.ToolAllowed(EditorContentMode.Level, "AI", true), "Level never exposes authored AI");
        check(!EditorContentRules.ToolAllowed(EditorContentMode.Level, "Bindings", false), "Story tools require story context");
        check(
            EditorContentRules.ToolAllowed(EditorContentMode.Level, "Bindings", true),
            "Existing campaign story context retains bindings"
        );
        var changed = SeasonCompiler.Copy(pack);
        changed.MapLayouts[0].Start = new();
        check(Reject(() => EditorContentRules.ValidateEdit(pack, changed)), "Level rejects custom starts at mutation boundary");
        changed.MapLayouts[0].Start = null;
        changed.MapLayouts[0].Checkpoints.Add(new());
        check(Reject(() => EditorContentRules.ValidateEdit(pack, changed)), "Level rejects checkpoints at mutation boundary");
        level.Start = new();
        level.Checkpoints.Add(new());
        level.Encounters.Add(new());
        changed = SeasonCompiler.Copy(pack);
        changed.MapLayouts[0].Name = "Renamed level";
        EditorContentRules.ValidateEdit(pack, changed);
        check(changed.MapLayouts[0].Checkpoints.Count == 1, "Legacy mission fields round-trip without activation or deletion");
        changed.MapLayouts[0].Encounters.Add(new());
        check(Reject(() => EditorContentRules.ValidateEdit(pack, changed)), "Level rejects authored encounter edits");
        pack.Missions.Add(new() { Id = Id(22), LayoutId = level.Id });
        check(EditorContentRules.Mode(pack, level.Id) == EditorContentMode.Mission, "Legacy mission ownership selects Mission Editor");
        var mixedChoice = new EditorDraftChoice { Mode = EditorContentRules.Mode(pack), Modes = EditorContentRules.AvailableModes(pack) };
        check(
            EditorContentRules.Includes(mixedChoice, EditorContentMode.Mission)
                && EditorContentRules.Includes(mixedChoice, EditorContentMode.Level),
            "Legacy campaign missions remain discoverable alongside the new level option"
        );
        check(MapLayerRules.ForCharacter(new[] { pack }, "", "woods") == null, "Mission-owned layouts never run as ordinary layers");
        check(MapLayerRules.ContentForCharacter(new[] { pack }, "", "woods").Count == 0, "Mission zones and exits stay out of levels");
        pack.MissionPackage = new MissionPackage { AllowStandalonePlay = true };
        check(EditorContentRules.Mode(pack) == EditorContentMode.Mission, "Standalone packages use Mission Editor");
        check(
            EditorContentRules.AvailableModes(pack).SequenceEqual(new[] { EditorContentMode.Mission }),
            "Standalone mission packages never appear under Levels"
        );
        pack.MissionPackage = null;
        pack.Missions.Clear();
        level.Exit = new() { Id = Id(30), Location = "woods" };
        pack.Zones.Add(
            new()
            {
                Id = Id(40),
                LayoutId = level.Id,
                Location = "woods",
                RequiredQuestId = Id(60),
            }
        );
        pack.Zones.Add(new() { Id = Id(41), Location = "woods" });
        var second = SeasonCompiler.Copy(pack);
        second.Id = Id(11);
        second.Zones[0].Id = Id(42);
        var content = MapLayerRules.ContentForCharacter(new[] { pack, second }, "", "woods");
        check(content.Count == 2 && content.All(c => c.Extract != null), "Each enabled source preserves its additional extract");
        check(content.Select(c => c.Source).Distinct().Count() == 2, "Content snapshots retain source package and layout identities");
        check(content.All(c => c.Zones.Count == 1), "Only owned zones enter enabled level snapshots");
        content[0].Zones[0].Name = "Snapshot edit";
        check(pack.Zones[0].Name != "Snapshot edit", "Snapshots do not mutate published zones");
        var overrides = new Dictionary<string, bool> { [MapLayerRules.Key(pack.Id, level.Id)] = false };
        check(
            MapLayerRules.ContentForCharacter(new[] { pack, second }, "", "woods", overrides).Count == 1,
            "Disabled level contributes no zones or extract"
        );
        check(MapLayerRules.ContentForCharacter(new[] { pack }, "", "Woods").Count == 0, "Level content map matching stays exact");
        second.Zones[0].Id = pack.Zones[0].Id;
        check(
            Reject(() => MapLayerRules.ContentForCharacter(new[] { pack, second }, "", "woods")),
            "Conflicting zone identities cannot share interaction state"
        );
        foreach (var status in new[] { "Started", "AvailableForFinish" })
            check(EditorContentRules.QuestEligible(Id(60), status), "Active native quest permits level interaction: " + status);
        foreach (var status in new string?[] { null, "Locked", "AvailableForStart", "Success", "Fail" })
            check(!EditorContentRules.QuestEligible(Id(60), status), "Inactive native quest cannot use gated interaction: " + status);
        check(EditorContentRules.QuestEligible("", null), "Ungated level interactions require no campaign or quest");
    }
}
