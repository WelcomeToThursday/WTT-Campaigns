using UnityEngine;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal sealed partial class EncounterPatrolRuntime
{
    private static int PreviousWaypoint(MapPatrolRoute route, int to, int direction) =>
        route.Completion == MapPatrolRoute.Loop ? (to + route.Waypoints.Count - 1) % route.Waypoints.Count : to - direction;

    private static EncounterPathStatus CheckMovementPath(Squad squad, PatrolBotSnapshot snapshot, int to, int direction)
    {
        var member = squad.Members.Find(m => m.Id == snapshot.BotId)!;
        var route = squad.State.Route;
        var from = PreviousWaypoint(route, to, direction);
        var position = EncounterNavigation.ToVector3(snapshot.Position);
        var target = EncounterNavigation.ToVector3(route.Waypoints[to].Position);
        var saved = member.Path.SavedCorners(position);
        // A leader can advance while a follower is still on the previous leg.
        // Check that member's own remainder before the newly requested curve.
        if (
            saved.Length >= 2
            && (member.PathEndpointWaypoint == to || member.PathEndpointWaypoint == from)
            && member.Navigation.PatrolPathClear(saved)
        )
        {
            if (member.PathEndpointWaypoint == to)
                return EncounterPathStatus.Complete;
            var next = member.Navigation.EvaluateRoute(route, from, to);
            if (next.Status != EncounterPathStatus.Complete)
                member.PathStatus = "Squad route check: " + next.Reason;
            return next.Status;
        }

        if (from < 0 || from >= route.Waypoints.Count)
        {
            var reachable = member.Navigation.TryPatrolPath(position, target, out _, out var reason);
            if (!reachable)
                member.PathStatus = "Squad route check: " + reason;
            return reachable ? EncounterPathStatus.Complete : EncounterPathStatus.Failed;
        }
        var curve = member.Navigation.EvaluateRoute(route, from, to);
        if (curve.Status != EncounterPathStatus.Complete)
        {
            member.PathStatus = "Squad route check: " + curve.Reason;
            return curve.Status;
        }
        // Initial joins and recovery use a connector to the curve, just as
        // movement does. Never require a generic path to the far waypoint.
        var points = new System.Numerics.Vector3[curve.Corners.Length];
        for (var i = 0; i < points.Length; i++)
            points[i] = SplineGeometry.Vector(curve.Corners[i]);
        var cursor = new SplineFollower();
        cursor.Select(from, to, points, new(position.x, position.y, position.z));
        var entry = EncounterNavigation.ToVector3(curve.Corners[cursor.NextSample]);
        var connected = member.Navigation.TryPatrolPath(position, entry, out _, out var connectorReason);
        if (!connected)
            member.PathStatus = "Squad curve connector: " + connectorReason;
        return connected ? EncounterPathStatus.Complete : EncounterPathStatus.Failed;
    }

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
        var from = PreviousWaypoint(route, to, direction);
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
