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

        try
        {
            // Capture positions are feet positions.  Lift the capsule a small amount so
            // touching the floor does not make a valid standing point look obstructed.
            var bottom = world + Vector3.up * (AgentRadius + 0.05f);
            var top = world + Vector3.up * (AgentHeight - AgentRadius + 0.05f);
            return ClearAuthoredBarriers(world, world)
                && !Physics.CheckCapsule(bottom, top, AgentRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
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

    internal bool HasCompletePath(Vector3 from, Vector3 to)
    {
        try
        {
            var path = new NavMeshPath();
            if ((from - to).sqrMagnitude <= 0.0001f)
            {
                // Unity reports one corner for a zero-length path.  It is still a complete
                // path when both endpoints are on the same NavMesh surface.
                return NavMesh.SamplePosition(from, out _, PointTolerance, NavMeshAreaMask)
                    && NavMesh.SamplePosition(to, out _, PointTolerance, NavMeshAreaMask);
            }

            if (!NavMesh.CalculatePath(from, to, NavMeshAreaMask, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            var corners = path.corners;
            return corners != null && corners.Length >= 2 && ClearSegments(corners);
        }
        catch
        {
            return false;
        }
    }

    internal static Vector3 ToVector3(SpatialVector value)
    {
        return new Vector3(value.X, value.Y, value.Z);
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
