using Newtonsoft.Json;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Spatial;

public sealed class MapLayout
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New map layout";
    public string Location { get; set; } = "";
    public bool ApplyInNormalRaids { get; set; }

    public bool ShouldSerializeApplyInNormalRaids() => ApplyInNormalRaids;

    public List<MapObjectEdit> Objects { get; set; } = new();
    public List<MapLootPlacement> Loot { get; set; } = new();
    public List<MapDoorEdit> Doors { get; set; } = new();
    public List<MapVolume> Barriers { get; set; } = new();
    public SpatialCapture? Start { get; set; }
    public List<MapVolume> Checkpoints { get; set; } = new();
    public MapVolume? Exit { get; set; }
    public SpatialSpline? PlayerRouteSpline { get; set; }

    public bool ShouldSerializePlayerRouteSpline() => PlayerRouteSpline != null;

    // AI authoring is layout-owned and intentionally separate from player route checkpoints.
    public List<SpatialCapture> SpawnPoints { get; set; } = new();
    public List<MapEncounter> Encounters { get; set; } = new();
    public List<MapPatrolRoute> PatrolRoutes { get; set; } = new();

    [System.Runtime.Serialization.OnDeserialized]
    private void RestoreLegacyDoorPlacements(System.Runtime.Serialization.StreamingContext context)
    {
        // The first door-placement build captured native doors with the default Prop kind.
        // Only door captures populated NativeId on Prop targets; preserve all other edits.
        if (Objects == null || Doors == null)
            return;
        foreach (var edit in Objects.ToArray())
        {
            if (
                edit.Operation != "Copy"
                || edit.Target?.Kind != "Prop"
                || string.IsNullOrEmpty(edit.Target.NativeId)
                || edit.Target.IsAsset
                || !string.IsNullOrEmpty(edit.Target.Bundle)
                || !string.IsNullOrEmpty(edit.Target.Template)
            )
                continue;
            if (Doors.Exists(d => d.Id == edit.Id))
                continue; // Keep conflicting data for normal validation to report.
            edit.Target.Kind = "Door";
            Doors.Add(
                new MapDoorEdit
                {
                    Id = edit.Id,
                    Name = edit.Name,
                    Location = edit.Location,
                    Scene = edit.Scene,
                    ObjectPath = edit.ObjectPath,
                    Position = edit.Position,
                    Rotation = edit.Rotation,
                    Target = edit.Target,
                    PlaceNew = true,
                    State = "Shut",
                }
            );
            Objects.Remove(edit);
        }
    }
}

public sealed class MapTarget
{
    public string Bundle { get; set; } = "";
    public string Asset { get; set; } = "";

    public bool ShouldSerializeBundle() => !string.IsNullOrEmpty(Bundle);

    public bool ShouldSerializeAsset() => !string.IsNullOrEmpty(Asset);

    [JsonIgnore]
    public bool IsAsset => Kind is "AssetProp" or "AssetContainer";
    public string Kind { get; set; } = "Prop";
    public string Template { get; set; } = "";
    public SpatialVector Origin { get; set; } = new();
    public string Scene { get; set; } = "";
    public string Path { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string NativeId { get; set; } = "";
}

public sealed class MapLootPlacement : SpatialCapture
{
    public List<NativeItem> Items { get; set; } = new();
}

public static class SceneTargetRules
{
    public static bool Matches(MapTarget saved, MapTarget actual)
    {
        if (
            saved.Kind != actual.Kind
            || saved.Scene != actual.Scene
            || saved.Template != actual.Template
            || saved.Fingerprint != actual.Fingerprint
        )
            return false;
        if (saved.NativeId.Length > 0)
            return saved.NativeId == actual.NativeId;
        if (saved.Kind != "Loot")
            return saved.Path == actual.Path;
        if (saved.Origin?.Finite != true || actual.Origin?.Finite != true)
            return false;
        var x = saved.Origin.X - actual.Origin.X;
        var y = saved.Origin.Y - actual.Origin.Y;
        var z = saved.Origin.Z - actual.Origin.Z;
        return x * x + y * y + z * z < .15f * .15f;
    }
}

public sealed class MapObjectEdit : SpatialCapture
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ContainerSettings? Container { get; set; }
    public MapTarget Target { get; set; } = new();
    public string Operation { get; set; } = "Move";
    public SpatialVector Scale { get; set; } =
        new()
        {
            X = 1,
            Y = 1,
            Z = 1,
        };
}

public sealed class ContainerSettings
{
    public string Mode { get; set; } = "Native";
    public string LootPool { get; set; } = "";
    public int SpawnChance { get; set; } = 100;
    public bool Locked { get; set; }
    public string KeyTemplate { get; set; } = "";
    public List<ContainerContent> Contents { get; set; } = new();
}

public sealed class ContainerContent
{
    public string Template { get; set; } = "";
    public int Count { get; set; } = 1;
}

public sealed class MapDoorEdit : SpatialCapture
{
    public MapDoorEdit() => Name = "Door";

    public MapTarget Target { get; set; } = new();
    public string State { get; set; } = "Unchanged";
    public bool PlaceNew { get; set; }

    // Null preserves the map's key; empty explicitly removes its key requirement.
    public string? KeyId { get; set; }
    public bool? CanBeBreached { get; set; }
    public bool? Operatable { get; set; }

    public bool ShouldSerializePlaceNew() => PlaceNew;

    public bool ShouldSerializeKeyId() => KeyId != null;

    public bool ShouldSerializeCanBeBreached() => CanBeBreached.HasValue;

    public bool ShouldSerializeOperatable() => Operatable.HasValue;
}

public sealed class MapVolume : SpatialCapture
{
    public string Shape { get; set; } = "Box";
    public SpatialVector Size { get; set; } =
        new()
        {
            X = 2,
            Y = 2,
            Z = 2,
        };
    public float Radius { get; set; } = 2;
}

public static class MapLayoutRules
{
    public static bool NeedsFormat9(MapLayout layout) => layout.Objects?.AsValueEnumerable().Any(o => o?.Container != null) == true;

    public static bool NeedsFormat8(MapLayout layout) =>
        layout.Objects?.AsValueEnumerable().Any(o => o?.Target?.IsAsset == true || (o != null && SceneAssetRules.IsContainer(o))) == true;

    public static bool NeedsFormat5(MapLayout layout) =>
        layout.Loot?.Count > 0 || layout.Objects?.AsValueEnumerable().Any(o => o?.Target?.Kind != "Prop") == true;

    public static bool NeedsFormat6(MapLayout layout) =>
        layout.SpawnPoints?.Count > 0 || layout.Encounters?.Count > 0 || layout.PatrolRoutes?.Count > 0;

    public static int Format(IEnumerable<MapLayout> layouts) =>
        layouts.AsValueEnumerable().Any(l => l.PlayerRouteSpline != null || l.PatrolRoutes.AsValueEnumerable().Any(r => r.Spline != null))
            ? 12
        : layouts.AsValueEnumerable().Any(NeedsFormat9) ? 9
        : layouts.AsValueEnumerable().Any(NeedsFormat8) ? 8
        : layouts.AsValueEnumerable().Any(NeedsFormat6) ? 6
        : layouts.AsValueEnumerable().Any(NeedsFormat5) ? 5
        : 4;

    public static IEnumerable<SpatialCapture> Points(MapLayout layout)
    {
        foreach (var point in layout.Objects ?? new())
            yield return point;
        foreach (var door in layout.Doors ?? new())
            if (door != null && door.PlaceNew)
                yield return door;
        foreach (var point in layout.Loot ?? new())
            yield return point;
        foreach (var point in layout.Barriers ?? new())
            yield return point;
        foreach (var point in layout.Checkpoints ?? new())
            yield return point;
        if (layout.Start != null)
            yield return layout.Start;
        if (layout.Exit != null)
            yield return layout.Exit;
        foreach (var point in layout.SpawnPoints ?? new())
            yield return point;
        foreach (var route in layout.PatrolRoutes ?? new())
        foreach (var point in route?.Waypoints ?? new())
            yield return point;
        foreach (var encounter in layout.Encounters ?? new())
            if (encounter?.Trigger?.Volume != null)
                yield return encounter.Trigger.Volume;
    }

    public static IEnumerable<string> OwnedIds(MapLayout layout)
    {
        yield return layout.Id;
        foreach (var point in Points(layout))
            yield return point.Id;
        foreach (var door in layout.Doors ?? new())
            if (door != null && !door.PlaceNew)
                yield return door.Id;
        foreach (var loot in layout.Loot ?? new())
            if (loot != null)
                foreach (var item in loot.Items ?? new())
                    yield return item.Id;
        foreach (var route in layout.PatrolRoutes ?? new())
            if (route != null)
                yield return route.Id;
        foreach (var encounter in layout.Encounters ?? new())
        {
            if (encounter == null)
                continue;
            yield return encounter.Id;
            foreach (var wave in encounter.Waves ?? new())
                if (wave != null)
                    yield return wave.Id;
            foreach (var wave in encounter.Waves ?? new())
                if (wave != null)
                    foreach (var entry in wave.Roster ?? new())
                        if (entry != null)
                            yield return entry.Id;
        }
    }

    public static bool Positive(SpatialVector? v) => v?.Finite == true && v.X > 0 && v.Y > 0 && v.Z > 0;

    public static List<string> Errors(MapLayout layout, bool walkthrough = false)
    {
        var errors = new List<string>();
        void Need(bool valid, string error)
        {
            if (!valid)
                errors.Add(error);
        }
        Need(SeasonValidator.IsId(layout.Id), "Layout identity is invalid.");
        Need(!string.IsNullOrWhiteSpace(layout.Name) && layout.Name.Length <= 120, "Name is required (up to 120 characters).");
        Need(!string.IsNullOrWhiteSpace(layout.Location) && layout.Location.Length <= 120, "Choose a map.");
        if (
            layout.Loot == null
            || layout.Loot.AsValueEnumerable().Any(x => x == null)
            || layout.Objects == null
            || layout.Doors == null
            || layout.Barriers == null
            || layout.Checkpoints == null
            || layout.SpawnPoints == null
            || layout.Encounters == null
            || layout.PatrolRoutes == null
            || layout.Objects.AsValueEnumerable().Any(o => o == null)
            || layout.Doors.AsValueEnumerable().Any(d => d == null)
            || layout.Barriers.AsValueEnumerable().Any(v => v == null)
            || layout.Checkpoints.AsValueEnumerable().Any(v => v == null)
            || layout.SpawnPoints.AsValueEnumerable().Any(p => p == null)
            || layout.Encounters.AsValueEnumerable().Any(e => e == null)
            || layout.PatrolRoutes.AsValueEnumerable().Any(r => r == null)
        )
        {
            errors.Add("Layout collections cannot contain null records.");
            return errors;
        }
        var splineError = RouteSpline.Error(layout.PlayerRouteSpline, RouteSpline.PlayerAnchors(layout), false);
        Need(splineError.Length == 0, splineError);
        var ids = OwnedIds(layout).AsValueEnumerable().ToArray();
        Need(
            ids.Length <= 2000
                && ids.AsValueEnumerable().All(SeasonValidator.IsId)
                && ids.AsValueEnumerable().Distinct().Count() == ids.Length,
            "Layout records require unique identities (at most 2000 records)."
        );
        foreach (var point in Points(layout))
        {
            Need(
                point.Location == layout.Location && !string.IsNullOrWhiteSpace(point.Scene),
                "Record map/scene does not match: " + point.Name
            );
            Need(point.Position?.Finite == true && point.Rotation?.Finite == true, "Invalid transform: " + point.Name);
            if (point is MapVolume v)
                Need(
                    v.Shape is "Box" or "Sphere" && Positive(v.Size) && float.IsFinite(v.Radius) && v.Radius > 0,
                    "Invalid volume: " + point.Name
                );
        }
        foreach (var edit in layout.Objects)
        {
            Need(edit.Operation is "Move" or "Hide" or "Copy", "Unknown object operation: " + edit.Name);
            Need(
                edit.Target != null && edit.Target.Kind is "Prop" or "Loot" or "Container" or "AssetProp" or "AssetContainer",
                "Unknown scene target kind."
            );
            Need(
                edit.Operation != "Copy" || edit.Target?.Kind is "Prop" or "Container" || edit.Target?.IsAsset == true,
                "Only props and supported assets can be copied."
            );
            Need(
                edit.Target?.IsAsset != true || (edit.Operation == "Copy" && SceneAssetRules.Valid(edit.Target)),
                "Invalid asset placement reference."
            );
            Need(
                !SceneAssetRules.IsContainer(edit) || (edit.Scale != null && edit.Scale.X == 1 && edit.Scale.Y == 1 && edit.Scale.Z == 1),
                "Native containers retain their original size."
            );
            Need(
                !SceneAssetRules.IsContainer(edit) || SeasonValidator.IsId(edit.Target.Template),
                "Placed containers require a native template identity."
            );
            Need(Positive(edit.Scale), "Invalid object scale: " + edit.Name);
            if (edit.Container is { } settings)
            {
                Need(SceneAssetRules.IsContainer(edit), "Container settings require a placed loot container.");
                Need(settings.Mode is "Native" or "Fixed" or "Empty", "Unknown container loot mode.");
                Need(settings.SpawnChance is >= 0 and <= 100, "Container spawn chance must be between 0 and 100.");
                Need(settings.LootPool == "" || SeasonValidator.IsId(settings.LootPool), "Invalid container loot pool.");
                Need(settings.KeyTemplate == "" || SeasonValidator.IsId(settings.KeyTemplate), "Invalid container key.");
                Need(!settings.Locked || SeasonValidator.IsId(settings.KeyTemplate), "Choose a key for the locked container.");
                Need(settings.Contents != null && settings.Contents.Count <= 100, "A container supports up to 100 fixed item entries.");
                foreach (var content in settings.Contents ?? new())
                    Need(
                        content != null && SeasonValidator.IsId(content.Template) && content.Count is > 0 and <= 10000,
                        "Invalid fixed container item or quantity."
                    );
            }
        }
        foreach (var loot in layout.Loot)
        {
            var validation = new SeasonValidationResult();
            SeasonValidator.ItemTree(loot.Items, "Placed loot", validation);
            foreach (var issue in validation.Issues)
                errors.Add(issue.Message);
        }
        foreach (var door in layout.Doors)
        {
            Need(door.State is "Unchanged" or "Open" or "Shut" or "Locked", "Unknown door state: " + door.Name);
            Need(
                door.KeyId == null || door.KeyId.Length <= 128 && door.KeyId.Trim() == door.KeyId,
                "Invalid door key identifier: " + door.Name
            );
            Need(!door.PlaceNew || door.Target?.Kind == "Door", "New doors require a native door source: " + door.Name);
        }

        foreach (var error in MapEncounterRules.Errors(layout))
            errors.Add(error);
        foreach (
            var target in layout
                .Objects.AsValueEnumerable()
                .Select(o => o.Target)
                .Concat(layout.Doors.AsValueEnumerable().Select(d => d.Target))
        )
            Need(
                target != null
                    && (
                        target.IsAsset
                            ? SceneAssetRules.Valid(target)
                            : !string.IsNullOrWhiteSpace(target.Scene)
                                && !string.IsNullOrWhiteSpace(target.Path)
                                && target.Fingerprint?.Length == 64
                    ),
                "Capture or rebind the scene target."
            );
        foreach (var target in layout.Objects.AsValueEnumerable().Select(o => o.Target).Where(t => t != null && t.Kind != "Prop"))
        {
            Need(target.Origin?.Finite == true, "Invalid original loot position.");
            Need(
                target.Template != null && target.Template.Length <= 120 && target.NativeId != null && target.NativeId.Length <= 256,
                "Invalid native target identity."
            );
            Need(target.Kind != "Container" || !string.IsNullOrWhiteSpace(target.NativeId), "Containers require a stable native identity.");
        }
        var targets = layout
            .Objects.AsValueEnumerable()
            .Where(o => o.Operation != "Copy" && o.Target != null)
            .Select(o => o.Target.Scene + "/" + o.Target.Path)
            .Concat(
                layout
                    .Doors.AsValueEnumerable()
                    .Where(d => d.Target != null && !d.PlaceNew)
                    .Select(d => d.Target.Scene + "/" + d.Target.Path)
            )
            .ToArray();
        Need(targets.AsValueEnumerable().Distinct().Count() == targets.Length, "A scene object has conflicting overrides.");
        Need(
            !targets
                .AsValueEnumerable()
                .Any(a => targets.AsValueEnumerable().Any(b => a != b && a.StartsWith(b + "/", StringComparison.Ordinal))),
            "Edit a parent or its children, not both in one layout."
        );
        if (walkthrough)
        {
            Need(
                layout.Start != null && layout.Exit != null && layout.Checkpoints.Count > 0,
                "Walkthrough requires a start, at least one checkpoint and an exit."
            );
        }
        return errors;
    }
}

// Shared transaction semantics, exercised offline with fake scene adapters.
public sealed class SceneEditTransaction : IDisposable
{
    private readonly Stack<Action> _restore = new();

    public void Apply(Action apply, Action restore)
    {
        _restore.Push(restore);
        try
        {
            apply();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var errors = new List<Exception>();
        while (_restore.Count > 0)
            try
            {
                _restore.Pop()();
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        if (errors.Count > 0)
            throw new AggregateException("Scene restoration needs attention.", errors);
    }
}
