using System.Numerics;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Authoring.Controllers;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class SplineChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static SpatialCapture Point(string id, float x, float z) =>
            new()
            {
                Id = id,
                Position = new() { X = x, Z = z },
            };
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .0001f;
        var anchors = new List<SpatialCapture> { Point("a", 0, 0), Point("b", 4, 0), Point("c", 4, 4) };
        var spline = RouteSpline.Create(anchors, false);
        check(
            Near(SplineGeometry.Evaluate(spline, 0, 0), Vector3.Zero) && Near(SplineGeometry.Evaluate(spline, 0, 1), new(4, 0, 0)),
            "Spline endpoints retain their anchors"
        );
        check(Math.Abs(SplineGeometry.Distance(spline) - 8) < .001f, "Corner mode preserves exact legacy polyline distance");
        SplineGeometry.Smooth(spline, 1, .5f);
        check(
            Vector3.Dot(SplineGeometry.Tangent(spline, 0, 1), SplineGeometry.Tangent(spline, 1, 0)) > .9999f,
            "Auto handles have a continuous tangent through a fixed anchor"
        );
        check(Near(SplineGeometry.Vector(spline.Knots[1].Position), new(4, 0, 0)), "Smoothing never moves authored anchors");
        spline.Knots[0].Mode = SplineKnot.Free;
        spline.Knots[0].Outgoing.Z = 2;
        var before = SeasonCompiler.Copy(spline);
        var inserted = SplineGeometry.Split(spline, 0, .37f);
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100f;
            var splitPoint =
                t <= .37f ? SplineGeometry.Evaluate(spline, 0, t / .37f) : SplineGeometry.Evaluate(spline, 1, (t - .37f) / .63f);
            check(Near(splitPoint, SplineGeometry.Evaluate(before, 0, t)), "Segment insertion preserves the original curve at " + i);
        }
        check(
            inserted.AnchorId.Length == 0 && inserted.Id != spline.Knots[0].Id,
            "Shaping points have independent identities and no gameplay binding"
        );
        var samples = SplineGeometry.Sample(spline);
        check(
            samples.Zip(samples.Skip(1)).All(pair => Vector3.Distance(pair.First.Position, pair.Second.Position) <= .5001f),
            "Adaptive sampling bounds every movement chord"
        );
        check(samples.Any(p => p.Position.Z > .1f), "Adaptive samples retain authored curve detail");
        var reverse = SeasonCompiler.Copy(spline);
        SplineGeometry.Reverse(reverse);
        for (var segment = 0; segment < SplineGeometry.SegmentCount(spline); segment++)
        for (var step = 0; step <= 10; step++)
            check(
                Near(
                    SplineGeometry.Evaluate(spline, segment, step / 10f),
                    SplineGeometry.Evaluate(reverse, SplineGeometry.SegmentCount(spline) - 1 - segment, 1 - step / 10f)
                ),
                "Reversing swaps handles and preserves geometry"
            );
        var aligned = spline.Knots[1];
        aligned.Mode = SplineKnot.Aligned;
        SplineGeometry.SetHandle(aligned, true, new(2, 0, 1));
        check(
            Vector3.Dot(
                Vector3.Normalize(SplineGeometry.Vector(aligned.Incoming)),
                Vector3.Normalize(SplineGeometry.Vector(aligned.Outgoing))
            ) < -.999f,
            "Aligned handles retain opposing directions"
        );
        aligned.Mode = SplineKnot.Free;
        var outgoing = SplineGeometry.Vector(aligned.Outgoing);
        SplineGeometry.SetHandle(aligned, true, Vector3.UnitY);
        check(Near(outgoing, SplineGeometry.Vector(aligned.Outgoing)), "Free handles can be edited independently");

        var route = new MapPatrolRoute
        {
            Waypoints = anchors,
            Completion = MapPatrolRoute.PingPong,
            WaitSeconds = new() { 2, 0, 3 },
            Spline = spline,
        };
        var leg = RouteSpline.Leg(route, 0, 1);
        var back = RouteSpline.Leg(route, 1, 0);
        check(
            Near(leg[0].Position, back[^1].Position) && Near(leg[^1].Position, back[0].Position),
            "Ping-pong evaluates reversed spline geometry"
        );
        check(
            EncounterMovementPolicy.Continuation(route, 0, 1) == -1 && EncounterMovementPolicy.Continuation(route, 1, 1) == 2,
            "Spline shaping does not alter waits or continuous waypoint progression"
        );
        var oldHandles = SeasonCompiler.Copy(spline.Knots[0].Outgoing);
        anchors[0].Position.Y = 1;
        RouteSpline.Synchronize(spline, anchors, false);
        check(
            spline.Knots[0].Position.Y == 1 && spline.Knots[0].Outgoing.Z == oldHandles.Z,
            "Moving anchors retains relative manual handles"
        );
        MapPatrolRouteEditing.Reverse(route);
        RouteSpline.Synchronize(spline, anchors, false);
        check(
            RouteSpline.Error(spline, anchors, false) == "" && route.WaitSeconds.SequenceEqual(new[] { 3f, 0, 2 }),
            "Route reversal preserves anchor bindings and waits"
        );
        MapPatrolRouteEditing.Move(route, 0, 1);
        RouteSpline.Synchronize(spline, anchors, false);
        check(
            RouteSpline.Error(spline, anchors, false) == "" && route.WaitSeconds[1] == 3,
            "Reordering retains the moved anchor identity and wait"
        );
        MapPatrolRouteEditing.RemoveAt(route, 1);
        RouteSpline.Synchronize(spline, anchors, false);
        check(
            RouteSpline.Error(spline, anchors, false) == "" && spline.Knots.All(k => k.AnchorId != "c"),
            "Removing a route marker removes stale spline bindings"
        );

        var closed = RouteSpline.Create(new[] { Point("one", 0, 0), Point("two", 5, 0), Point("three", 5, 5) }, true);
        var loop = new MapPatrolRoute
        {
            Waypoints = new() { Point("one", 0, 0), Point("two", 5, 0), Point("three", 5, 5) },
            Spline = closed,
        };
        SplineGeometry.Split(closed, 2, .5f);
        MapPatrolRouteEditing.Reverse(loop);
        check(
            RouteSpline.Error(closed, loop.Waypoints, true) == "" && RouteSpline.Leg(loop, 2, 0).Count > 2,
            "Closed route reversal retains the closing shaping block"
        );
        var round = new SpatialSpline
        {
            Knots = new()
            {
                new() { Position = new() },
                new() { Position = new() { X = 4 } },
                new()
                {
                    Position = new() { X = 4, Z = 4 },
                },
            },
        };
        check(SplineGeometry.RoundCorner(round, 1, .5f) && round.Knots.Count == 4, "Navigation corners round into a tangent arc");
        check(
            SplineGeometry.Evaluate(round, 1, .5f).X < 4 && SplineGeometry.Evaluate(round, 1, .5f).Z > 0,
            "Rounding replaces the corner with an inset curve"
        );
        var degenerate = RouteSpline.Create(new[] { Point("x", 1, 1), Point("y", 1, 1) }, false);
        SplineGeometry.Smooth(degenerate, 0, .5f);
        check(
            SplineGeometry.Sample(degenerate).Count == 2 && SplineGeometry.Distance(degenerate) == 0,
            "Repeated points produce finite bounded geometry"
        );
        degenerate.Knots[0].Incoming.X = float.NaN;
        check(SplineGeometry.Error(degenerate).Contains("finite"), "Nonfinite imported handles are rejected");

        var player = new MapLayout
        {
            Start = Point("start", 0, 0),
            Exit = new MapVolume
            {
                Id = "exit",
                Position = new() { X = 4 },
            },
        };
        check(
            MapLayoutRules.Format(new[] { player }) == 4 && !JsonConvert.SerializeObject(player).Contains("PlayerRouteSpline"),
            "Legacy player routes retain their format and serialization"
        );
        player.PlayerRouteSpline = RouteSpline.Create(RouteSpline.PlayerAnchors(player), false);
        check(MapLayoutRules.Format(new[] { player }) == 12, "Spline content requires campaign format 12");
        var json = JsonConvert.SerializeObject(player);
        var restored = JsonConvert.DeserializeObject<MapLayout>(json)!;
        check(
            restored.PlayerRouteSpline!.Knots[0].Id == player.PlayerRouteSpline.Knots[0].Id && !json.Contains("Samples"),
            "Only authored spline controls round-trip, with stable identity"
        );
        var history = SeasonCompiler.Copy(player);
        player.PlayerRouteSpline.Knots[0].Outgoing.X = 9;
        player = history;
        check(player.PlayerRouteSpline!.Knots[0].Outgoing.X == 0, "Document snapshots isolate spline edits for undo and redo");

        Validation(check);
        Suspension(check);
        Transactions(check);
    }

    private static void Transactions(Action<bool, string> check)
    {
        var layout = new MapLayout
        {
            Id = "layout",
            Start = new() { Id = "start" },
            Exit = new()
            {
                Id = "exit",
                Position = new() { X = 4 },
            },
        };
        layout.PlayerRouteSpline = RouteSpline.Create(RouteSpline.PlayerAnchors(layout), false);
        var definition = new SeasonDefinition
        {
            Missions = new() { new() { LayoutId = layout.Id } },
            MapLayouts = new() { layout },
        };
        var session = new RaidEditorSession("woods")
        {
            Definition = definition,
            Baseline = SeasonCompiler.Copy(definition),
            Hold = true,
        };
        session.Edit(d =>
        {
            var map = d.MapLayouts[0];
            SplineGeometry.Split(map.PlayerRouteSpline!, 0, .5f);
            map.Start!.Position.X = 1;
        });
        check(
            session.Definition!.FormatVersion == 12 && session.Definition.MapLayouts[0].PlayerRouteSpline!.Knots[0].Position.X == 1,
            "Actual authoring transactions synchronize spline anchors and promote the campaign format"
        );
        session.Undo(false);
        check(
            session.Definition!.MapLayouts[0].PlayerRouteSpline!.Knots.Count == 2
                && session.Definition.MapLayouts[0].Start!.Position.X == 0,
            "One session undo restores both spline geometry and the moved anchor"
        );
        session.Undo(true);
        check(
            session.Definition!.MapLayouts[0].PlayerRouteSpline!.Knots.Count == 3
                && session.Definition.MapLayouts[0].Start!.Position.X == 1,
            "Session redo restores the complete spline edit"
        );
        var original = session.Definition.MapLayouts[0];
        var duplicate = EditorMapRecords.DuplicateLayout(original);
        check(
            RouteSpline.Error(duplicate.PlayerRouteSpline, RouteSpline.PlayerAnchors(duplicate), false) == ""
                && duplicate.PlayerRouteSpline!.Knots[0].AnchorId == duplicate.Start!.Id
                && duplicate.PlayerRouteSpline.Knots[0].Id != original.PlayerRouteSpline!.Knots[0].Id,
            "Layout duplication remaps anchor bindings and gives curve knots fresh identities"
        );
    }

    private sealed class Navigation(Func<SpatialVector, SpatialVector, bool> reachable) : IPatrolNavigation
    {
        public bool CanReach(SpatialVector a, SpatialVector b) => reachable(a, b);
    }

    private static void Suspension(Action<bool, string> check)
    {
        var route = new MapPatrolRoute
        {
            Id = "route",
            Completion = MapPatrolRoute.Stop,
            Waypoints = new()
            {
                new() { Id = "a" },
                new()
                {
                    Id = "b",
                    Position = new() { X = 5 },
                },
                new()
                {
                    Id = "c",
                    Position = new() { X = 10 },
                },
            },
        };
        route.Spline = RouteSpline.Create(route.Waypoints, false);
        var state = new EncounterPatrolStateMachine(route);
        state.Start(new[] { "leader" });
        state.Restore(
            new()
            {
                RouteId = route.Id,
                Waypoint = 1,
                Direction = 1,
            },
            0
        );
        var bots = new[]
        {
            new PatrolBotSnapshot
            {
                BotId = "leader",
                Alive = true,
                ControlState = PatrolBotControlState.Eligible,
                Position = new(),
            },
        };
        var update = state.Update(bots, 1, new Navigation((_, to) => to.X != 5));
        check(
            update.Status == PatrolRuntimeStatus.Suspended && update.TargetWaypointIndex == 1 && !update.Rejoined,
            "Blocked spline targets suspend without skipping to a different reachable waypoint"
        );
        update = state.Update(bots, 3, new Navigation((_, _) => true));
        check(
            update.Status == PatrolRuntimeStatus.Moving && update.TargetWaypointIndex == 1,
            "Cleared spline target resumes the saved waypoint"
        );
        state.Restore(
            new()
            {
                RouteId = route.Id,
                Waypoint = 1,
                Direction = 1,
                WaitRemaining = 5,
            },
            10
        );
        state.SuspendNavigation(12);
        check(state.Capture(100).WaitRemaining == 3, "Navigation suspension freezes the remaining waypoint wait");
        state.Update(bots, 100, new Navigation((_, _) => true));
        check(state.Capture(101).WaitRemaining == 2, "Navigation recovery resumes the saved wait rather than restarting it");
    }

    private static void Validation(Action<bool, string> check)
    {
        LivePreview(check);
        GroundFollowing(check);
        var samples = new List<SplineSample>();
        for (var i = 0; i <= 20; i++)
            samples.Add(new(new(i * .5f, 0, 0), 0, i / 20f));
        var pass = new SplinePathValidation(samples, p => p, (_, _) => true);
        var quota = 3;
        pass.Advance(() => quota-- > 0);
        check(
            pass.Pending && pass.Points.Count == 3 && pass.Error == "",
            "Budget deferral keeps validation pending without calling it blocked"
        );
        while (pass.Pending)
        {
            quota = 3;
            pass.Advance(() => quota-- > 0);
        }
        check(
            pass.Complete && pass.Points.Count == samples.Count,
            "Resumable validation preserves exactly the samples used for following and display"
        );
        var wall = new SplinePathValidation(samples, p => p, (a, b) => b.X < 3);
        wall.Advance(() => true);
        check(
            !wall.Complete && wall.Error.Contains("wall") && wall.FailedSample == 6,
            "A wall fails at its actual curve segment without detouring"
        );
        var floor = new SplinePathValidation(samples, p => p + Vector3.UnitY, (_, _) => true);
        floor.Advance(() => true);
        check(!floor.Complete && floor.FailedSample == 0, "Projection cannot jump to a different floor");
        var tolerance = new SplinePathValidation(samples, p => p + Vector3.UnitY * .05f, (_, _) => true);
        tolerance.Advance(() => true);
        check(tolerance.Complete, "Small ground tolerance is accepted without changing the authored spline");
        var standing = new SplinePathValidation(samples, p => p, (_, _) => true, p => p.X < 3);
        standing.Advance(() => true);
        check(
            standing.Failure == SplinePathFailure.StandingClearance
                && standing.FailedSample == 6
                && standing.Error.Contains("standing clearance"),
            "Standing clearance reports its own failure category and exact sample"
        );
        check(
            floor.Failure == SplinePathFailure.OffNavMesh && wall.Failure == SplinePathFailure.BlockedSegment,
            "Off-mesh floors and blocked chords retain distinct diagnostics"
        );
        var absentGround = new SplinePathValidation(
            samples,
            _ => null,
            (_, _) => true,
            _ => throw new Exception("No standing check without ground")
        );
        absentGround.Advance(() => true);
        check(absentGround.Failure == SplinePathFailure.OffNavMesh, "Missing mesh is reported before standing clearance");
        var warningColors = new HashSet<uint>();
        foreach (
            var failure in new[] { SplinePathFailure.OffNavMesh, SplinePathFailure.StandingClearance, SplinePathFailure.BlockedSegment }
        )
        {
            var warning = new EncounterPathResult(
                EncounterPathStatus.Failed,
                samples.Select(s => SplineGeometry.Spatial(s.Position)).ToArray(),
                "problem",
                failure,
                6
            );
            warningColors.Add(warning.DiagnosticColor);
            check(
                warning.HasProblemLocation
                    && warning.SegmentColor(6) == warning.DiagnosticColor
                    && warning.SegmentColor(9) != warning.DiagnosticColor
                    && warning.SegmentColor(20) == 0xBFCBD4,
                failure + ": warning peaks at the failing sample and fades to unconfirmed grey with route distance"
            );
        }
        check(warningColors.Count == 3, "Ground, standing and segment failures use distinct high-contrast colors");
        var unavailable = new EncounterPathResult(EncounterPathStatus.Failed, reason: "Navigation unavailable");
        check(
            !unavailable.HasProblemLocation && unavailable.SegmentColor(0) == unavailable.DiagnosticColor,
            "Unlocalized errors never invent a problem marker"
        );
        var follower = new SplineFollower();
        var points = samples.Select(s => s.Position).ToArray();
        follower.Select(0, 1, points, new(2, 0, 0));
        follower.Advance(9);
        follower.Select(0, 1, points, new(0, 0, 0));
        check(follower.NextSample == 9, "Combat interruption cannot reset an existing curve leg's progress");
        follower.Select(1, 2, points, new(1, 0, 0));
        check(follower.NextSample == 2, "A new leg creates its own connector and progress cursor");
        follower.BeginLeg(2, 3);
        follower.Advance(4);
        follower.Select(2, 3, points, points[^1]);
        check(
            follower.NextSample == 4,
            "Promoting a continuous leg retains its mapped native cursor instead of rejoining at a nearest endpoint"
        );
        var route = new MapPatrolRoute
        {
            Completion = MapPatrolRoute.Stop,
            Waypoints = new()
            {
                new() { Id = "a" },
                new()
                {
                    Id = "b",
                    Position = new() { X = 10 },
                },
            },
        };
        route.Spline = RouteSpline.Create(route.Waypoints, false);
        var inspection = new PatrolRouteInspection();
        var calls = 0;
        EncounterPathResult Curve(MapPatrolRoute _, int from, int to) =>
            ++calls == 1
                ? new(EncounterPathStatus.Pending)
                : new(EncounterPathStatus.Complete, new[] { route.Waypoints[from].Position, route.Waypoints[to].Position });
        inspection.Refresh(route, "route", 1, 1, 0, 0, (_, _) => throw new Exception("Spline inspection must not detour"), true, Curve);
        check(inspection.Pending, "Patrol inspection waits for the curve validator");
        inspection.Refresh(route, "route", 1, 1, 1, .1, (_, _) => throw new Exception("Spline inspection must not detour"), true, Curve);
        check(
            !inspection.Pending && inspection.Segments[0].Result.Distance == 10,
            "Patrol inspection uses the curve result rather than another path query"
        );
        var displayed = inspection.Segments[0].Result;
        var caption = inspection.Summary(true);
        EncounterPathResult PendingCurve(MapPatrolRoute _, int from, int to) => new(EncounterPathStatus.Pending);
        inspection.Refresh(route, "route", 1, 1, 2, 3, (_, _) => throw new Exception("Unexpected detour"), true, PendingCurve);
        check(
            inspection.Pending && ReferenceEquals(displayed, inspection.Segments[0].Result) && inspection.Summary(true) == caption,
            "Background route validation keeps settled geometry and labels visible without a pending-status flash"
        );
        inspection.Refresh(
            route,
            "route",
            1,
            1,
            3,
            3.1,
            (_, _) => throw new Exception("Unexpected detour"),
            true,
            (_, _, _) => new(EncounterPathStatus.Failed, reason: "Passage blocked")
        );
        check(
            !inspection.Pending && inspection.Summary(false).Contains("Passage blocked"),
            "Quiet refresh still publishes newly detected route errors"
        );
        inspection.Refresh(route, "route", 2, 1, 4, 3.2, (_, _) => throw new Exception("Unexpected detour"), true, PendingCurve);
        check(
            inspection.Segments[0].Result.Status == EncounterPathStatus.Pending
                && !inspection.Summary(true).Contains("Passage blocked")
                && !inspection.Summary(true).Contains("Checking")
                && !inspection.Segments[0].Caption.Contains("checking"),
            "An authored geometry edit clears stale results without announcing background validation"
        );
    }

    private static void GroundFollowing(Action<bool, string> check)
    {
        var spline = new SpatialSpline
        {
            Knots = new()
            {
                new()
                {
                    Mode = SplineKnot.Free,
                    Outgoing = new() { X = 3, Y = 3 },
                },
                new()
                {
                    Mode = SplineKnot.Free,
                    Position = new() { X = 10 },
                    Incoming = new() { X = -3, Y = 3 },
                },
            },
        };
        var samples = SplineGeometry.Sample(spline);
        var authored = samples.Select(s => s.Position).ToArray();
        static Vector3? Flat(Vector3 p) => new(p.X, 0, p.Z);
        var strict = new SplinePathValidation(samples, Flat, (_, _) => true);
        strict.Advance(() => true);
        check(!strict.Complete, "Legacy strict projection still rejects height drift");
        var grounded = new SplinePathValidation(samples, Flat, (_, _) => true, _ => true, Flat);
        while (grounded.Pending)
        {
            var quota = 3;
            grounded.Advance(() => quota-- > 0);
        }
        check(
            grounded.Complete
                && grounded.Points.Select((p, i) => p.Y == 0 && p.X == samples[i].Position.X && p.Z == samples[i].Position.Z).All(v => v),
            "Raised Bezier handles follow the ground without changing horizontal shape across budgeted batches"
        );
        check(
            authored.SequenceEqual(samples.Select(s => s.Position)),
            "Ground following leaves authored controls and source samples intact"
        );

        var ramp = new List<SplineSample>();
        for (var i = 0; i <= 20; i++)
            ramp.Add(new(new(i * .5f, i == 20 ? 2.5f : 0, 0), 0, i / 20f));
        static Vector3? Slope(Vector3 p) => new(p.X, p.X * .25f, p.Z);
        var slopes = new SplinePathValidation(ramp, Slope, (_, _) => true, _ => true, Slope);
        slopes.Advance(() => true);
        check(
            slopes.Complete && slopes.Points.All(p => p.Y == p.X * .25f),
            "Connected ramps follow changing ground height between fixed anchors"
        );
        ramp.Reverse();
        var downhill = new SplinePathValidation(ramp, Slope, (_, _) => true, _ => true, Slope);
        downhill.Advance(() => true);
        check(
            downhill.Complete && slopes.Points.SequenceEqual(downhill.Points.AsEnumerable().Reverse()),
            "Reverse travel follows the same grounded samples"
        );

        var missing = new SplinePathValidation(samples, Flat, (_, _) => true, _ => true, p => p.X >= 3 ? null : Flat(p));
        missing.Advance(() => true);
        check(missing.Failure == SplinePathFailure.OffNavMesh, "Ground following cannot bridge missing mesh");
        var sideways = new SplinePathValidation(samples, Flat, (_, _) => true, _ => true, p => new(p.X + .2f, 0, p.Z));
        sideways.Advance(() => true);
        check(
            sideways.Failure == SplinePathFailure.OffNavMesh,
            "Ground following cannot pull an invalid curve sideways onto walkable ground"
        );
        var stacked = new SplinePathValidation(samples, Flat, (_, _) => true, _ => true, p => new(p.X, 2, p.Z));
        stacked.Advance(() => true);
        check(stacked.Failure == SplinePathFailure.OffNavMesh, "Ground following cannot jump to an upper floor");
        var disconnected = new SplinePathValidation(samples, Flat, (a, b) => b.X < 3, _ => true, Flat);
        disconnected.Advance(() => true);
        check(
            disconnected.Failure == SplinePathFailure.BlockedSegment,
            "Ground following still requires chord continuity across gaps and obstacles"
        );
        var clearance = new SplinePathValidation(samples, Flat, (_, _) => true, p => p.X < 3, Flat);
        clearance.Advance(() => true);
        check(clearance.Failure == SplinePathFailure.StandingClearance, "Ground following retains standing clearance rejection");
        check(
            clearance.FailedPosition?.Y == 0 && samples[clearance.FailedSample].Position.Y > .1f,
            "Grounded clearance warnings mark the actual floor location, not the raised authored curve"
        );
        var middle = samples.Count / 2;
        var anchored = new SplinePathValidation(samples, Flat, (_, _) => true, _ => true, Flat, new HashSet<int> { middle });
        anchored.Advance(() => true);
        check(
            anchored.Failure == SplinePathFailure.OffNavMesh && anchored.FailedSample == middle,
            "Whole-route smoothing cannot silently move an intermediate waypoint to another floor"
        );
        var raisedStart = samples.ToList();
        raisedStart[0] = new(new(0, 2, 0), 0, 0);
        var wrongStart = new SplinePathValidation(raisedStart, Flat, (_, _) => true, _ => true, Flat);
        wrongStart.Advance(() => true);
        check(
            wrongStart.Failure == SplinePathFailure.OffNavMesh && wrongStart.FailedSample == 0,
            "Ground following keeps the original strict floor tolerance at route anchors"
        );
    }

    private static void LivePreview(Action<bool, string> check)
    {
        var budget = new EditorCurveBudget();
        check(
            Enumerable.Range(0, 64).All(_ => budget.TryTake(1, 0)) && !budget.TryTake(1, 0),
            "Editor curve checks share a 64-sample allowance across legs in one frame"
        );
        check(
            budget.TryTake(2, 1) && !budget.TryTake(2, 1.003) && budget.TryTake(3, 2),
            "Editor curve checks yield at the time limit and resume on the next frame"
        );
        foreach (var completion in new[] { MapPatrolRoute.Stop, MapPatrolRoute.Loop, MapPatrolRoute.PingPong })
        {
            var route = new MapPatrolRoute
            {
                Completion = completion,
                Waypoints = new()
                {
                    new() { Id = "a" },
                    new()
                    {
                        Id = "b",
                        Position = new() { X = 10 },
                    },
                    new()
                    {
                        Id = "c",
                        Position = new() { X = 10, Z = 10 },
                    },
                },
            };
            route.Spline = RouteSpline.Create(route.Waypoints, completion == MapPatrolRoute.Loop);
            SplineGeometry.Smooth(route.Spline, 1, .5f);
            SplineGeometry.Split(route.Spline, 1, .4f);
            var inspection = new PatrolRouteInspection();
            var queries = 0;
            EncounterPathResult Pending(MapPatrolRoute _, int from, int to)
            {
                queries++;
                return new(EncounterPathStatus.Pending);
            }
            void Refresh(long revision, bool ready) =>
                inspection.Refresh(
                    route,
                    "route",
                    revision,
                    1,
                    (int)revision,
                    revision,
                    (_, _) => throw new Exception("Unexpected detour"),
                    ready,
                    Pending
                );
            Refresh(1, false);
            check(
                queries == 0 && inspection.Pending && inspection.Segments.All(s => s.Result.Corners.Length > 2),
                completion + ": all authored legs draw immediately even when navigation is not ready"
            );
            Refresh(2, true);
            check(
                queries == 1 && inspection.Segments.All(s => s.Result.Status == EncounterPathStatus.Pending && s.Result.Corners.Length > 2),
                completion + ": one deferred leg cannot hide any live curve preview"
            );
            foreach (var segment in inspection.Segments)
            {
                var expected = RouteSpline.Leg(route, segment.From, segment.To);
                check(
                    expected.Count == segment.Result.Corners.Length
                        && expected
                            .Select((s, i) => Vector3.Distance(s.Position, SplineGeometry.Vector(segment.Result.Corners[i])) < .0001f)
                            .All(s => s),
                    completion + ": preview matches movement samples including shaping points, reverse legs and loop closure"
                );
            }
            var original = inspection.Segments[^1].Result.Corners.Select(SplineGeometry.Vector).ToArray();
            route.Waypoints[2].Position.Z = 15;
            Refresh(3, false);
            check(
                !original.SequenceEqual(inspection.Segments[^1].Result.Corners.Select(SplineGeometry.Vector)),
                completion + ": moving an anchor updates later legs before navigation validation finishes"
            );
            route.Waypoints[2].Position.Z = 10;
            Refresh(4, false);
            check(
                original.SequenceEqual(inspection.Segments[^1].Result.Corners.Select(SplineGeometry.Vector)),
                completion + ": canceling a live anchor edit restores its original preview"
            );
        }
    }
}
