using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>
/// NavMesh adapter used by both draft validation and the encounter runtime.  The authored point is
/// always passed to the native spawner; SamplePosition is used only to validate a small tolerance.
/// </summary>
public sealed class EncounterNavigation : IEncounterNavigation, IPatrolNavigation
{
    // Validation must reject a point that would cause the native creator to snap to a
    // different floor or room.  The authored position is never replaced at activation.
    private const float PointTolerance = 0.10f;
    private const float AgentRadius = 0.30f;
    private const float AgentHeight = 1.75f;
    private const int NavMeshAreaMask = NavMesh.AllAreas;
    private readonly RaycastHit[] _pathHits = new RaycastHit[64];
    private readonly Func<MapLayout?>? _layout;

    public EncounterNavigation(Func<MapLayout?>? layout = null)
    {
        _layout = layout;
    }

    public bool IsOnNavMesh(SpatialVector position)
    {
        if (!TryToVector(position, out var world))
            return false;

        try
        {
            return NavMesh.SamplePosition(world, out var hit, PointTolerance, NavMeshAreaMask)
                && (hit.position - world).sqrMagnitude <= PointTolerance * PointTolerance;
        }
        catch
        {
            // A map can be between teardown and scene activation.  Unknown navigation is invalid.
            return false;
        }
    }

    public bool HasStandingClearance(SpatialVector position)
    {
        if (!TryToVector(position, out var world))
            return false;

        return HasStandingClearance(world, null);
    }

    /// <summary>
    /// Validates the standing capsule while allowing the caller's own player
    /// collider to remain in the world. Every other solid wall, prop, barrier
    /// or player blocks the authored position.
    /// </summary>
    internal bool HasStandingClearance(SpatialVector position, EFT.Player ignoredPlayer)
    {
        if (!TryToVector(position, out var world))
            return false;

        return HasStandingClearance(world, ignoredPlayer);
    }

    private bool HasStandingClearance(Vector3 world, EFT.Player? ignoredPlayer, bool ignorePlayers = false)
    {
        try
        {
            // Capture positions are feet positions.  Lift the capsule a small amount so
            // touching the floor does not make a valid standing point look obstructed.
            var bottom = world + Vector3.up * (AgentRadius + 0.05f);
            var top = world + Vector3.up * (AgentHeight - AgentRadius + 0.05f);
            if (!ClearAuthoredBarriers(world, world))
                return false;
            var colliders = Physics.OverlapCapsule(bottom, top, AgentRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (var collider in colliders)
            {
                if (!collider)
                    continue;
                var owner = collider.GetComponentInParent<EFT.Player>();
                if (owner != null && (ignorePlayers || (ignoredPlayer != null && ReferenceEquals(owner, ignoredPlayer))))
                    continue;
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool HasCompletePath(SpatialVector from, SpatialVector to)
    {
        return TryToVector(from, out var start) && TryToVector(to, out var target) && HasCompletePath(start, target);
    }

    public bool CanReach(SpatialVector from, SpatialVector to)
    {
        return HasCompletePath(from, to);
    }

    internal bool HasCompletePath(Vector3 from, Vector3 to) => EvaluatePath(from, to).Status == EncounterPathStatus.Complete;

    internal EncounterPathResult EvaluatePath(SpatialVector from, SpatialVector to) =>
        TryToVector(from, out var start) && TryToVector(to, out var end)
            ? EvaluatePath(start, end)
            : new(EncounterPathStatus.Failed, reason: "Invalid waypoint coordinates");

    private EncounterPathResult EvaluatePath(Vector3 from, Vector3 to)
    {
        try
        {
            if (
                !NavMesh.SamplePosition(from, out var start, PointTolerance, NavMeshAreaMask)
                || (start.position - from).sqrMagnitude > PointTolerance * PointTolerance
            )
                return new(EncounterPathStatus.Failed, reason: "Start waypoint is off NavMesh");
            if (
                !NavMesh.SamplePosition(to, out var end, PointTolerance, NavMeshAreaMask)
                || (end.position - to).sqrMagnitude > PointTolerance * PointTolerance
            )
                return new(EncounterPathStatus.Failed, reason: "End waypoint is off NavMesh");
            // Moving bots occupy their own origin and may share a target. Standing geometry
            // must be clear, but transient players are handled by native movement avoidance.
            if (!HasStandingClearance(from, null, true))
                return new(EncounterPathStatus.Failed, reason: "Start waypoint is obstructed");
            if (!HasStandingClearance(to, null, true))
                return new(EncounterPathStatus.Failed, reason: "End waypoint is obstructed");
            if ((from - to).sqrMagnitude <= .0001f)
                return new(EncounterPathStatus.Complete, new[] { Spatial(from), Spatial(to) });
            var path = new NavMeshPath();
            var found = NavMesh.CalculatePath(from, to, NavMeshAreaMask, path);
            var corners = path.corners;
            var points = new SpatialVector[corners.Length];
            for (var i = 0; i < corners.Length; i++)
                points[i] = Spatial(corners[i]);
            if (!found || path.status != NavMeshPathStatus.PathComplete)
                return new(EncounterPathStatus.Failed, points, found ? path.status.ToString() : "No path");
            if (corners.Length < 2 || (corners[corners.Length - 1] - to).sqrMagnitude > PointTolerance * PointTolerance)
                return new(EncounterPathStatus.Failed, points, "Path does not reach waypoint");
            if (!ClearSegments(corners))
                return new(EncounterPathStatus.Failed, points, "Path blocked by solid scenery or authored barrier");
            return new(EncounterPathStatus.Complete, points);
        }
        catch (Exception error)
        {
            return new(EncounterPathStatus.Failed, reason: "Navigation unavailable: " + error.Message);
        }
    }

    private static SpatialVector Spatial(Vector3 point) =>
        new()
        {
            X = point.x,
            Y = point.y,
            Z = point.z,
        };

    internal static Vector3 ToVector3(SpatialVector value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    internal bool TryPatrolPath(Vector3 from, Vector3 to, out Vector3[] corners, out string status)
    {
        var result = EvaluatePath(from, to);
        status = result.Status == EncounterPathStatus.Complete ? "PathComplete" : result.Reason;
        corners = Array.Empty<Vector3>();
        if (result.Status != EncounterPathStatus.Complete)
            return false;
        corners = new Vector3[result.Corners.Length];
        for (var i = 0; i < corners.Length; i++)
            corners[i] = ToVector3(result.Corners[i]);
        return true;
    }

    internal bool RemainingPathClear(Vector3 position, AbstractBotPath path)
    {
        if (path.CurIndex < 0 || path.CurIndex >= path.Length)
            return false;
        var corners = new Vector3[path.Length - path.CurIndex + 1];
        corners[0] = position;
        for (var i = path.CurIndex; i < path.Length; i++)
            corners[i - path.CurIndex + 1] = path.GetPoint(i);
        for (var i = 1; i < corners.Length; i++)
            if (NavMesh.Raycast(corners[i - 1], corners[i], out _, NavMeshAreaMask))
                return false;
        return ClearSegments(corners);
    }

    internal static bool TryToVector(SpatialVector? value, out Vector3 world)
    {
        if (value?.Finite == true)
        {
            world = ToVector3(value);
            return true;
        }

        world = default;
        return false;
    }

    internal static bool Contains(MapVolume volume, Vector3 world)
    {
        if (volume?.Position?.Finite != true || volume.Rotation?.Finite != true)
            return false;

        var center = ToVector3(volume.Position);
        var local = Quaternion.Inverse(Quaternion.Euler(ToVector3(volume.Rotation))) * (world - center);
        if (string.Equals(volume.Shape, "Sphere", StringComparison.OrdinalIgnoreCase))
            return volume.Radius > 0 && local.sqrMagnitude <= volume.Radius * volume.Radius;

        if (volume.Size?.Finite != true || volume.Size.X <= 0 || volume.Size.Y <= 0 || volume.Size.Z <= 0)
            return false;
        var half = ToVector3(volume.Size) * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private bool ClearSegments(Vector3[] corners)
    {
        // The baked NavMesh does not know about authored barriers or moved scenery.
        // Sweep the standing capsule along its solution rather than accepting paths through them.
        for (var index = 1; index < corners.Length; index++)
        {
            if (!ClearAuthoredBarriers(corners[index - 1], corners[index]))
                return false;
            var delta = corners[index] - corners[index - 1];
            var distance = delta.magnitude;
            if (distance <= .001f)
                continue;
            var bottom = corners[index - 1] + Vector3.up * (AgentRadius + .05f);
            var top = corners[index - 1] + Vector3.up * (AgentHeight - AgentRadius + .05f);
            var count = Physics.CapsuleCastNonAlloc(
                bottom,
                top,
                AgentRadius,
                delta / distance,
                _pathHits,
                distance,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            );
            if (count == _pathHits.Length)
                return false;
            for (var hit = 0; hit < count; hit++)
            {
                var collision = _pathHits[hit];
                if (!collision.collider || collision.collider.GetComponentInParent<EFT.Player>())
                    continue;
                // Walkable floors and slopes are expected contacts; walls and ceilings are not.
                if (collision.normal.y < .65f)
                    return false;
            }
        }
        return true;
    }

    private bool ClearAuthoredBarriers(Vector3 from, Vector3 to)
    {
        var barriers = _layout?.Invoke()?.Barriers;
        if (barriers == null)
            return true;
        // Editing barriers are render-only ghosts. Test the swept standing bounds against
        // their authored volumes as well as the live scene, without creating colliders.
        var centerOffset = Vector3.up * (AgentHeight * .5f + .05f);
        var capsuleHalf = Vector3.up * (AgentHeight * .5f - AgentRadius);
        foreach (var barrier in barriers)
        {
            if (barrier.Position?.Finite != true || barrier.Rotation?.Finite != true)
                return false;
            var inverse = Quaternion.Inverse(Quaternion.Euler(ToVector3(barrier.Rotation)));
            var start = inverse * (from + centerOffset - ToVector3(barrier.Position));
            var finish = inverse * (to + centerOffset - ToVector3(barrier.Position));
            var axis = inverse * capsuleHalf;
            Vector3 half;
            if (barrier.Shape == "Sphere")
                half = Vector3.one * barrier.Radius;
            else if (barrier.Shape == "Box" && barrier.Size?.Finite == true)
                half = ToVector3(barrier.Size) * .5f;
            else
                return false;
            // Conservatively enclose the capsule, including rotated barriers. A blocked
            // bound is rejected; authoring never silently shifts a bot around the barrier.
            half += new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)) + Vector3.one * AgentRadius;
            var delta = finish - start;
            var minimum = 0f;
            var maximum = 1f;
            var intersects = true;
            for (var dimension = 0; dimension < 3; dimension++)
            {
                if (Mathf.Abs(delta[dimension]) < .00001f)
                {
                    if (Mathf.Abs(start[dimension]) > half[dimension])
                    {
                        intersects = false;
                        break;
                    }
                    continue;
                }
                var first = (-half[dimension] - start[dimension]) / delta[dimension];
                var last = (half[dimension] - start[dimension]) / delta[dimension];
                minimum = Mathf.Max(minimum, Mathf.Min(first, last));
                maximum = Mathf.Min(maximum, Mathf.Max(first, last));
                if (minimum > maximum)
                {
                    intersects = false;
                    break;
                }
            }
            if (intersects)
                return false;
        }
        return true;
    }
}
