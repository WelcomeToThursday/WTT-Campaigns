using UnityEngine;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal sealed partial class EncounterPatrolRuntime
{
    private static void RememberCurveProgress(Member member)
    {
        if (member.CurveNativeStart >= 0 && member.Path.Owns(member.Mover))
            member.Curve.Advance(member.CurveSampleStart + Math.Max(0, member.Path.CurrentCorner - member.CurveNativeStart));
    }

    private static EncounterPathStatus BuildMovementPath(Member member, Vector3 target, out Vector3[] corners, out string reason)
    {
        var route = member.Squad.State.Route;
        var position = member.Bot.GetPlayer.Transform.position;
        corners = Array.Empty<Vector3>();
        if (route.Spline == null)
            return member.Navigation.TryPatrolPath(position, target, out corners, out reason)
                ? EncounterPathStatus.Complete
                : EncounterPathStatus.Failed;
        var to = member.PassedWaypoint ? member.ContinuationWaypoint : member.Command!.WaypointIndex;
        var direction = member.Squad.State.Capture(Time.time).Direction;
        var from = to - direction;
        if (route.Completion == MapPatrolRoute.Loop)
            from = (to + route.Waypoints.Count - 1) % route.Waypoints.Count;
        if (from < 0 || from >= route.Waypoints.Count)
        {
            member.CurveNativeStart = -1;
            return member.Navigation.TryPatrolPath(position, target, out corners, out reason)
                ? EncounterPathStatus.Complete
                : EncounterPathStatus.Failed;
        }
        var result = member.Navigation.EvaluateRoute(route, from, to);
        reason = result.Reason;
        if (result.Status != EncounterPathStatus.Complete)
            return result.Status;
        RememberCurveProgress(member);
        var numeric = new System.Numerics.Vector3[result.Corners.Length];
        for (var i = 0; i < numeric.Length; i++)
            numeric[i] = SplineGeometry.Vector(result.Corners[i]);
        member.Curve.Select(from, to, numeric, new(position.x, position.y, position.z));
        var start = member.Curve.NextSample;
        var entry = EncounterNavigation.ToVector3(result.Corners[start]);
        // Pathfinding is permitted only for the connector that joins the remaining authored curve.
        if (!member.Navigation.TryPatrolPath(position, entry, out var connector, out reason))
            return EncounterPathStatus.Failed;
        corners = new Vector3[connector.Length + result.Corners.Length - start - 1];
        Array.Copy(connector, corners, connector.Length);
        for (var i = start + 1; i < result.Corners.Length; i++)
            corners[connector.Length + i - start - 1] = EncounterNavigation.ToVector3(result.Corners[i]);
        member.CurveNativeStart = connector.Length - 1;
        member.CurveSampleStart = start;
        return EncounterPathStatus.Complete;
    }
}
