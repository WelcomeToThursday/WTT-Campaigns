using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class DoorAuthoringChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var old = JsonConvert.DeserializeObject<MapDoorEdit>("{\"Id\":\"old\",\"State\":\"Locked\"}")!;
        check(
            !old.PlaceNew && old.KeyId == null && old.CanBeBreached == null && old.Operatable == null,
            "Legacy door edits preserve native key, breach and interaction settings"
        );
        var native = new EFT.Interactive.Door();
        var adapter = new SceneDoorState(native);
        var edit = new MapDoorEdit
        {
            State = "Locked",
            KeyId = "chosen-key",
            CanBeBreached = false,
            Operatable = true,
        };
        adapter.Apply(edit);
        check(
            native.DoorState == EFT.Interactive.EDoorState.Locked
                && native.KeyId == "chosen-key"
                && !native.CanBeBreached
                && !native.CanInteractWithBreach,
            "Actual door adapter applies locked start, custom key and breach controls"
        );
        native.DoorState = EFT.Interactive.EDoorState.Open;
        var count = native.SyncCount;
        adapter.Apply(JsonConvert.DeserializeObject<MapDoorEdit>(JsonConvert.SerializeObject(edit))!);
        check(
            native.SyncCount == count && native.DoorState == EFT.Interactive.EDoorState.Open,
            "Reconciliation does not relock a door after native player interaction"
        );
        edit.KeyId = "";
        adapter.Apply(edit);
        check(native.KeyId == "", "Empty door key explicitly clears the native requirement");
        edit.KeyId = null;
        adapter.Apply(edit);
        check(native.KeyId == "original-key", "Original key restores the captured native requirement");
        adapter.Restore();
        check(
            native.DoorState == EFT.Interactive.EDoorState.Open
                && native.CurrentAngle == 90
                && native.KeyId == "original-key"
                && native.CanBeBreached
                && !native.CanInteractWithBreach
                && native.Operatable,
            "Undo/disposal restores all original door settings and angle"
        );
        native.Alive = false;
        count = native.SyncCount;
        adapter.Restore();
        check(native.SyncCount == count, "Unloaded door restoration is harmless");
        var angled = new EFT.Interactive.Door { CurrentAngle = 37 };
        new SceneDoorState(angled).Apply(new MapDoorEdit { KeyId = "chosen-key" });
        check(angled.CurrentAngle == 37, "Original starting state preserves the exact native angle when changing a key");
        var placement = new MapDoorEdit
        {
            Id = "placed",
            PlaceNew = true,
            KeyId = "chosen-key",
            CanBeBreached = false,
            Position = new SpatialVector { X = 12 },
            Target = new MapTarget { Kind = "Door", NativeId = "source" },
        };
        var copy = JsonConvert.DeserializeObject<MapDoorEdit>(JsonConvert.SerializeObject(placement))!;
        check(
            copy.PlaceNew && copy.Position.X == 12 && copy.KeyId == "chosen-key" && copy.CanBeBreached == false,
            "Door placement and key settings survive serialization"
        );
        var layout = new MapLayout
        {
            Id = "layout",
            Doors = new() { old, placement },
        };
        check(
            MapLayoutRules.Points(layout).Single() == placement && MapLayoutRules.OwnedIds(layout).Count(id => id == "placed") == 1,
            "Placed doors have editable poses and exactly one owned identity; existing doors keep native positions"
        );
        var roots = EditorLibraryTrees.Doors(new[] { ("oak", "Office door", "Floor 1"), ("steel", "Steel door", "Basement") });
        var tree = EditorTreeModel.Create("doors", roots, "Basement", new HashSet<string> { "doors:scene:Basement" });
        check(
            tree.SelectableCount == 2
                && tree.Visible.Count(n => n.Selectable) == 1
                && tree.Visible.Any(n => n.Id == "steel")
                && !tree.Visible.Any(n => n.Id == "oak"),
            "Door preview tree preserves scene grouping, selection identities and ancestor search"
        );
    }
}
