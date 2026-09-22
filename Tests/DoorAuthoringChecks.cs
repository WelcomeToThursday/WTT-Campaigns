using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class DoorAuthoringChecks
{
    private sealed class RegistryOwner
    {
        public readonly Dictionary<string, object> Entries = new();
    }

    internal static void Run(Action<bool, string> check)
    {
        const string shape = "UnityEngine.Transform:door|EFT.Interactive.Door:door|UnityEngine.Transform:handle";
        var inspected =
            "UnityEngine.Transform:door|EFT.Interactive.Door:door|DebugPlus.Utils.OverlayProvider:door|UnityEngine.Transform:handle";
        var variants = SceneDoorFingerprint.LegacyComponents(shape, shape, 2, "door").ToArray();
        check(variants.Contains(inspected), "Legacy door hashes can reproduce the inspection-only root component without creating it");
        var savedOpen = SceneDoorFingerprint.Legacy(inspected, "hinge", "open", "scale", "mesh:100");
        check(
            variants.Select(v => SceneDoorFingerprint.Legacy(v, "hinge", "open", "scale", "mesh:100")).Contains(savedOpen),
            "Legacy open-door binding retains its complete geometry hash when the inspection overlay is absent"
        );
        check(
            !variants.Select(v => SceneDoorFingerprint.Legacy(v, "other hinge", "open", "scale", "mesh:100")).Contains(savedOpen),
            "Legacy compatibility rejects a moved door"
        );
        check(
            !variants.Select(v => SceneDoorFingerprint.Legacy(v, "hinge", "open", "scale", "mesh:101")).Contains(savedOpen),
            "Legacy compatibility rejects changed door meshes"
        );
        var stable = SceneDoorFingerprint.Stable(shape, "hinge", "closed", "scale", "mesh:100", "Y|0|90");
        check(
            stable.Length == 64 && stable != savedOpen,
            "Stable door identity fits the existing format and is distinct from legacy pose hashes"
        );
        check(
            stable != SceneDoorFingerprint.Stable(shape, "hinge", "closed", "scale", "mesh:100", "Y|0|45"),
            "Stable door identity detects changed native hinge geometry"
        );
        var registryField = typeof(RegistryOwner).GetField(nameof(RegistryOwner.Entries))!;
        var localRegistry = new SceneDoorRegistry(null, registryField, true);
        localRegistry.Remove("door", new object());
        check(
            !localRegistry.Available && !localRegistry.Contains("door"),
            "Local playtest doors do not require a network world or reflect on a missing owner"
        );
        var rejected = false;
        try
        {
            _ = new SceneDoorRegistry(null, registryField, false);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        check(rejected, "A missing network registry remains an error for network games");
        var owner = new RegistryOwner();
        var registry = new SceneDoorRegistry(owner, registryField, true);
        var owned = new object();
        owner.Entries.Add("door", owned);
        registry.Remove("door", new object());
        check(registry.Available && registry.Contains("door"), "Door cleanup preserves another object's registry identity");
        registry.Remove("door", owned);
        check(!registry.Contains("door"), "Door cleanup releases its captured registry without rereading a torn-down world");
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
        var brokenPlacement = new MapLayout
        {
            Objects = new()
            {
                new()
                {
                    Id = "broken-door",
                    Name = "Office door",
                    Operation = "Copy",
                    Position = new() { X = 27 },
                    Rotation = new() { Y = 80 },
                    Target = new()
                    {
                        NativeId = "native-door",
                        Path = "map/door",
                        Fingerprint = "fingerprint",
                    },
                },
                new() { Id = "ordinary-prop", Operation = "Copy" },
                new()
                {
                    Id = "container",
                    Operation = "Copy",
                    Target = new() { Kind = "Container", NativeId = "container" },
                },
            },
        };
        var recovered = JsonConvert.DeserializeObject<MapLayout>(JsonConvert.SerializeObject(brokenPlacement))!;
        var recoveredDoor = recovered.Doors.Single();
        check(
            recovered.Objects.Count == 2
                && recoveredDoor.Id == "broken-door"
                && recoveredDoor.PlaceNew
                && recoveredDoor.Target.Kind == "Door"
                && recoveredDoor.Target.NativeId == "native-door"
                && recoveredDoor.Target.Fingerprint == "fingerprint"
                && recoveredDoor.Position.X == 27
                && recoveredDoor.Rotation.Y == 80,
            "Existing misplaced prop records reload as native doors with their identity, binding and authored pose intact"
        );
        var recoveredAgain = JsonConvert.DeserializeObject<MapLayout>(JsonConvert.SerializeObject(recovered))!;
        check(
            recoveredAgain.Doors.Count == 1 && recoveredAgain.Objects.Count == 2,
            "Door repair is idempotent and preserves ordinary props and native containers"
        );
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
