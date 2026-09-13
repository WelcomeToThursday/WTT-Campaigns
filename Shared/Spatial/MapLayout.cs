using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Spatial;

public sealed class MapLayout
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New map layout";
    public string Location { get; set; } = "";
    public List<MapObjectEdit> Objects { get; set; } = new();
    public List<MapDoorEdit> Doors { get; set; } = new();
    public List<MapVolume> Barriers { get; set; } = new();
    public SpatialCapture? Start { get; set; }
    public List<MapVolume> Checkpoints { get; set; } = new();
    public MapVolume? Exit { get; set; }
}

public sealed class MapTarget
{
    public string Scene { get; set; } = "";
    public string Path { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string NativeId { get; set; } = "";
}

public sealed class MapObjectEdit : SpatialCapture
{
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

public sealed class MapDoorEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Door";
    public MapTarget Target { get; set; } = new();
    public string State { get; set; } = "Unchanged";
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
    public static IEnumerable<SpatialCapture> Points(MapLayout layout) =>
        layout
            .Objects.Cast<SpatialCapture>()
            .Concat(layout.Barriers)
            .Concat(layout.Checkpoints)
            .Concat(layout.Start == null ? Array.Empty<SpatialCapture>() : new[] { layout.Start })
            .Concat(layout.Exit == null ? Array.Empty<SpatialCapture>() : new[] { layout.Exit });

    public static IEnumerable<string> OwnedIds(MapLayout layout) =>
        new[] { layout.Id }.Concat(Points(layout).Select(p => p.Id)).Concat(layout.Doors.Select(d => d.Id));

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
            layout.Objects == null
            || layout.Doors == null
            || layout.Barriers == null
            || layout.Checkpoints == null
            || layout.Objects.Any(o => o == null)
            || layout.Doors.Any(d => d == null)
            || layout.Barriers.Any(v => v == null)
            || layout.Checkpoints.Any(v => v == null)
        )
        {
            errors.Add("Layout collections cannot contain null records.");
            return errors;
        }
        var ids = OwnedIds(layout).ToArray();
        Need(
            ids.Length <= 2000 && ids.All(SeasonValidator.IsId) && ids.Distinct().Count() == ids.Length,
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
            Need(Positive(edit.Scale), "Invalid object scale: " + edit.Name);
        }
        foreach (var door in layout.Doors)
            Need(door.State is "Unchanged" or "Open" or "Shut" or "Locked", "Unknown door state: " + door.Name);
        foreach (var target in layout.Objects.Select(o => o.Target).Concat(layout.Doors.Select(d => d.Target)))
            Need(
                target != null
                    && !string.IsNullOrWhiteSpace(target.Scene)
                    && !string.IsNullOrWhiteSpace(target.Path)
                    && target.Fingerprint?.Length == 64,
                "Capture or rebind the scene target."
            );
        var targets = layout
            .Objects.Where(o => o.Operation != "Copy" && o.Target != null)
            .Select(o => o.Target.Scene + "/" + o.Target.Path)
            .Concat(layout.Doors.Where(d => d.Target != null).Select(d => d.Target.Scene + "/" + d.Target.Path))
            .ToArray();
        Need(targets.Distinct().Count() == targets.Length, "A scene object has conflicting overrides.");
        Need(
            !targets.Any(a => targets.Any(b => a != b && a.StartsWith(b + "/", StringComparison.Ordinal))),
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
