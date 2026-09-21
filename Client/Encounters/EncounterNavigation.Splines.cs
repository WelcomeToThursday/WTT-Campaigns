using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

public sealed partial class EncounterNavigation
{
    private sealed class CurveCheck
    {
        internal int Fingerprint;
        internal long Revision;
        internal float Expires;
        internal SplinePathValidation? Check;
        internal SpatialVector[] Authored = Array.Empty<SpatialVector>();
        internal EncounterPathResult Result = new(EncounterPathStatus.Pending);
        internal int PreviewCount;
    }

    private readonly Dictionary<(MapPatrolRoute Route, int From, int To), CurveCheck> _curves = new();
    private static readonly EncounterFrameBudget CurveBudget = new(8);
    private static readonly EditorCurveBudget PreviewBudget = new();
    internal bool EditorPreview;

    internal void InvalidateCurves() => _curves.Clear();

    internal EncounterPathResult EvaluateRoute(MapPatrolRoute route, int from, int to)
    {
        if (route.Spline == null)
            return EvaluatePath(route.Waypoints[from].Position, route.Waypoints[to].Position);
        var error = RouteSpline.Error(route.Spline, route.Waypoints, route.Completion == MapPatrolRoute.Loop);
        if (error.Length > 0)
            return new(EncounterPathStatus.Failed, reason: error);
        var key = (route, from, to);
        var fingerprint = CurveFingerprint(route);
        if (
            !_curves.TryGetValue(key, out var entry)
            || entry.Fingerprint != fingerprint
            || entry.Revision != SceneNavigation.Revision
            || (entry.Result.Status != EncounterPathStatus.Pending && Time.realtimeSinceStartup >= entry.Expires)
        )
        {
            if (_curves.Count > 512)
                _curves.Clear();
            entry = new() { Fingerprint = fingerprint, Revision = SceneNavigation.Revision };
            _curves[key] = entry;
            try
            {
                var samples = RouteSpline.Leg(route, from, to);
                entry.Authored = new SpatialVector[samples.Count];
                for (var i = 0; i < samples.Count; i++)
                    entry.Authored[i] = SplineGeometry.Spatial(samples[i].Position);
                entry.Check = CreateCurveCheck(samples);
                entry.Result = new(EncounterPathStatus.Pending, entry.Authored, "Checking curve…");
            }
            catch (Exception e)
            {
                entry.Result = new(EncounterPathStatus.Failed, reason: e.Message);
                entry.Expires = Time.realtimeSinceStartup + 2;
            }
        }
        if (entry.Result.Status == EncounterPathStatus.Pending && entry.Check is { } check && Time.frameCount >= SceneNavigation.ReadyFrame)
        {
            try
            {
                check.Advance(() =>
                    EditorPreview
                        ? PreviewBudget.TryTake(
                            Time.frameCount,
                            (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency
                        )
                        : CurveBudget.TryTake(Time.frameCount) && EncounterNavigationBudget.Move()
                );
            }
            catch (Exception e)
            {
                entry.Result = new(EncounterPathStatus.Failed, entry.Authored, "Navigation unavailable: " + e.Message);
                entry.Check = null;
                entry.Expires = Time.realtimeSinceStartup + 2;
                return entry.Result;
            }
            // Publish ground-following samples as each budgeted batch finishes.
            // The eventual native path uses these same points.
            for (; entry.PreviewCount < check.Points.Count; entry.PreviewCount++)
                entry.Authored[entry.PreviewCount] = SplineGeometry.Spatial(check.Points[entry.PreviewCount]);
            if (!check.Pending)
            {
                if (check.FailedSample >= 0 && check.FailedPosition is { } failedPosition)
                    entry.Authored[check.FailedSample] = SplineGeometry.Spatial(failedPosition);
                var points = check.Complete ? new SpatialVector[check.Points.Count] : entry.Authored;
                if (check.Complete)
                    for (var i = 0; i < points.Length; i++)
                        points[i] = SplineGeometry.Spatial(check.Points[i]);
                entry.Result = new(
                    check.Complete ? EncounterPathStatus.Complete : EncounterPathStatus.Failed,
                    points,
                    check.Error,
                    check.Failure,
                    check.FailedSample
                );
                entry.Expires = Time.realtimeSinceStartup + 2;
            }
        }
        return entry.Result;
    }

    private System.Numerics.Vector3? ProjectCurvePoint(System.Numerics.Vector3 authored)
    {
        var world = new Vector3(authored.X, authored.Y, authored.Z);
        if (
            !NavMesh.SamplePosition(world, out var hit, PointTolerance, NavMeshAreaMask)
            || (hit.position - world).sqrMagnitude > PointTolerance * PointTolerance
        )
            return null;
        return new(hit.position.x, hit.position.y, hit.position.z);
    }

    private bool CurveStandingClearance(System.Numerics.Vector3 point) =>
        HasStandingClearance(new Vector3(point.X, point.Y, point.Z), null, true);

    private System.Numerics.Vector3? ProjectGroundCurvePoint(System.Numerics.Vector3 query)
    {
        var world = new Vector3(query.X, query.Y, query.Z);
        if (!NavMesh.SamplePosition(world, out var hit, .75f, NavMeshAreaMask))
            return null;
        var dx = hit.position.x - world.x;
        var dz = hit.position.z - world.z;
        if (dx * dx + dz * dz > .000101f)
            return null;
        return new(hit.position.x, hit.position.y, hit.position.z);
    }

    internal Vector3 GroundCurveEdit(Vector3 position, Vector3 previous)
    {
        // Free/X/Z drags keep their previous floor reference. Explicit Y edits
        // remain available when the author deliberately changes floors.
        var hit = ProjectGroundCurvePoint(new(position.x, previous.y, position.z));
        position.y = hit?.Y ?? previous.y;
        return position;
    }

    private SplinePathValidation CreateCurveCheck(IReadOnlyList<SplineSample> samples, ISet<int>? anchors = null) =>
        new(samples, ProjectCurvePoint, ClearCurveSegment, CurveStandingClearance, ProjectGroundCurvePoint, anchors);

    private bool ClearCurveSegment(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        var from = new Vector3(a.X, a.Y, a.Z);
        var to = new Vector3(b.X, b.Y, b.Z);
        return !NavMesh.Raycast(from, to, out _, NavMeshAreaMask) && ClearSegment(from, to, EncounterRouteClearance.Radius);
    }

    internal SplinePathValidation CheckCurve(SpatialSpline spline)
    {
        var samples = SplineGeometry.Sample(spline);
        var anchors = new HashSet<int>();
        for (var i = 1; i < samples.Count; i++)
            if (samples[i].T == 1 && spline.Knots[(samples[i].Segment + 1) % spline.Knots.Count].AnchorId.Length > 0)
                anchors.Add(i);
        return CreateCurveCheck(samples, anchors);
    }

    public string SplineError(MapPatrolRoute route)
    {
        if (route.Spline == null)
            return "";
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            if (i + 1 == route.Waypoints.Count && route.Completion != MapPatrolRoute.Loop)
                break;
            var result = EvaluateRoute(route, i, (i + 1) % route.Waypoints.Count);
            if (result.Status != EncounterPathStatus.Complete)
                return result.Reason;
            if (route.Completion == MapPatrolRoute.PingPong)
            {
                result = EvaluateRoute(route, i + 1, i);
                if (result.Status != EncounterPathStatus.Complete)
                    return result.Reason;
            }
        }
        return "";
    }

    internal async UniTask ValidateSplines(MapLayout layout, System.Threading.CancellationToken token)
    {
        foreach (var route in layout.PatrolRoutes)
        {
            if (route.Spline == null || route.Waypoints.Count < 2)
                continue;
            for (var i = 0; i < route.Waypoints.Count; i++)
            {
                if (i + 1 == route.Waypoints.Count && route.Completion != MapPatrolRoute.Loop)
                    break;
                await Leg(i, (i + 1) % route.Waypoints.Count);
                if (route.Completion == MapPatrolRoute.PingPong)
                    await Leg(i + 1, i);
            }
            async UniTask Leg(int from, int to)
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var result = EvaluateRoute(route, from, to);
                    if (result.Status == EncounterPathStatus.Failed)
                        throw new InvalidOperationException(route.Name + ": " + result.Reason);
                    if (result.Status == EncounterPathStatus.Complete)
                    {
                        // This adapter belongs to this admission pass. Keep earlier legs valid while checking long routes.
                        _curves[(route, from, to)].Expires = float.PositiveInfinity;
                        return;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }
        }
    }

    private static int CurveFingerprint(MapPatrolRoute route)
    {
        var hash = new HashCode();
        hash.Add(route.Completion);
        foreach (var p in route.Waypoints)
        {
            hash.Add(p.Id);
            Add(p.Position);
        }
        if (route.Spline is { } s)
        {
            hash.Add(s.Closed);
            hash.Add(s.Strength);
            foreach (var k in s.Knots)
            {
                hash.Add(k.Id);
                hash.Add(k.AnchorId);
                hash.Add(k.Mode);
                Add(k.Position);
                Add(k.Incoming);
                Add(k.Outgoing);
            }
        }
        return hash.ToHashCode();
        void Add(SpatialVector p)
        {
            hash.Add(p.X);
            hash.Add(p.Y);
            hash.Add(p.Z);
        }
    }
}
