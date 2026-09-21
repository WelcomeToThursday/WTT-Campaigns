using System.Numerics;

namespace WTT.Campaigns.Shared.Spatial;

public enum SplinePathFailure
{
    None,
    InvalidGeometry,
    OffNavMesh,
    StandingClearance,
    BlockedSegment,
}

/// <summary>A resumable walkability check; no pathfinding or silent detours between samples.</summary>
public sealed class SplinePathValidation
{
    private readonly IReadOnlyList<SplineSample> _samples;
    private readonly Func<Vector3, Vector3?> _project;
    private readonly Func<Vector3, Vector3, bool> _clear;
    private readonly Func<Vector3, bool>? _standing;
    private readonly Func<Vector3, Vector3?>? _groundProject;
    private readonly ISet<int>? _anchors;
    private int _next;
    public List<Vector3> Points { get; } = new();
    public string Error { get; private set; } = "";
    public SplinePathFailure Failure { get; private set; }
    public int FailedSample { get; private set; } = -1;
    public Vector3? FailedPosition { get; private set; }
    public bool Pending => Error.Length == 0 && _next < _samples.Count;
    public bool Complete => !Pending && Error.Length == 0 && Points.Count >= 2;

    public SplinePathValidation(
        IReadOnlyList<SplineSample> samples,
        Func<Vector3, Vector3?> project,
        Func<Vector3, Vector3, bool> clear,
        Func<Vector3, bool>? standing = null,
        Func<Vector3, Vector3?>? groundProject = null,
        ISet<int>? anchors = null
    )
    {
        _samples = samples;
        _project = project;
        _clear = clear;
        _standing = standing;
        _groundProject = groundProject;
        _anchors = anchors;
        if (samples.Count < 2)
        {
            Error = "A curve needs at least two points.";
            Failure = SplinePathFailure.InvalidGeometry;
        }
    }

    public void Advance(Func<bool> admit)
    {
        while (Pending && admit())
        {
            var authored = _samples[_next].Position;
            var anchored = _next == 0 || _next == _samples.Count - 1 || _anchors?.Contains(_next) == true;
            var followGround = _groundProject != null && !anchored;
            // Begin on the authored anchor's floor, then follow it in small steps.
            // Tangent height must not select an unrelated floor above or below it.
            var query = followGround ? new Vector3(authored.X, Points[^1].Y, authored.Z) : authored;
            var point = followGround ? _groundProject!(query) : _project(query);
            if (
                point == null
                || !float.IsFinite(point.Value.X)
                || !float.IsFinite(point.Value.Y)
                || !float.IsFinite(point.Value.Z)
                || (
                    followGround
                        ? !GroundStep(authored, Points[^1], point.Value)
                        : Vector3.DistanceSquared(point.Value, authored) > .010001f
                )
            )
                Fail(
                    SplinePathFailure.OffNavMesh,
                    followGround
                        ? "No nearby walkable ground at this part of the curve."
                        : "Route point is off the navigation mesh or on a different floor.",
                    query
                );
            else if (_standing != null && !_standing(point.Value))
                Fail(SplinePathFailure.StandingClearance, "Curve has insufficient standing clearance.", point.Value);
            else if (Points.Count > 0 && !_clear(Points[^1], point.Value))
                Fail(SplinePathFailure.BlockedSegment, "Curve crosses a wall, floor edge, or blocked passage.", point.Value);
            else
            {
                Points.Add(point.Value);
                _next++;
            }
        }
    }

    // Ground following corrects height only. It cannot pull a curve sideways
    // onto a ledge, bridge a gap, or jump vertically to a stacked floor.
    private static bool GroundStep(Vector3 authored, Vector3 previous, Vector3 projected)
    {
        var dx = projected.X - authored.X;
        var dz = projected.Z - authored.Z;
        var stepX = authored.X - previous.X;
        var stepZ = authored.Z - previous.Z;
        var rise = .2f + 1.5f * (float)Math.Sqrt(stepX * stepX + stepZ * stepZ);
        return dx * dx + dz * dz <= .000101f && Math.Abs(projected.Y - previous.Y) <= Math.Min(.75f, rise);
    }

    private void Fail(SplinePathFailure failure, string reason, Vector3 position)
    {
        Failure = failure;
        FailedSample = _next;
        FailedPosition = position;
        Error = reason + " Adjust the nearby knot or handles.";
    }
}

public interface IEncounterSplineNavigation
{
    string SplineError(MapPatrolRoute route);
}
