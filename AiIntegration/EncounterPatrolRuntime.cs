using System.Reflection;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using SAIN.Components;
using SAIN.Interop;
using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Encounters;

internal sealed class EncounterPatrolRuntime
{
    private sealed class Member
    {
        internal BotOwner Bot = null!;
        internal BotMover Mover = null!;
        internal EFT.HealthSystem.ActiveHealthController Health = null!;
        internal bool DeathConfirmed;

        internal void OnDeath(EDamageType _)
        {
            DeathConfirmed = true;
            Squad.Replan = true;
            Squad.Planning = false;
            Squad.NextUpdate = 0;
        }

        internal Squad Squad = null!;
        internal string Id = "";
        internal PatrolMovementCommand? Command;
        internal bool OwnsNavigation;
        internal Vector3 Destination;
        internal float NextMove;
        internal string PathStatus = "Not requested";
        internal string LastWriter = "None";
        internal string LastRepath = "Initial path";
        internal int PathSubmissions;
        internal float NextSpacing;
        internal float SpacingPace = 1;
        internal bool SpacingHeld;
        internal Vector3 Heading;
        internal int HeadingWaypoint = -1;
        internal int MovementWaypoint = -1;
        internal int ContinuationWaypoint = -1;
        internal int ArrivalCorner = -1;
        internal bool PassedWaypoint;
        internal readonly EncounterNavigation Navigation = new();
        internal readonly EncounterMovementPath Path = new();
    }

    private sealed class Squad(MapPatrolRoute route)
    {
        internal readonly EncounterPatrolStateMachine State = new(route);
        internal readonly List<Member> Members = new();
        internal readonly List<PatrolBotSnapshot> Snapshots = new();
        internal readonly EncounterPlanningNavigation Navigation = new(new EncounterNavigation(), EncounterNavigationBudget.Plan);
        internal bool Planning;
        internal bool Replan = true;
        internal float NextUpdate;
    }

    private static readonly Dictionary<BotOwner, Member> Owners = new();
    private static readonly Dictionary<BotMover, Member> Movers = new();

    [ThreadStatic]
    private static BotMover? _writing;
    private static bool _installed;
    private readonly Dictionary<string, Squad> _squads = new(StringComparer.Ordinal);
    private readonly MapLayout _layout;
    private readonly List<Squad> _schedule = new();
    private int _nextSquad;
    internal string Status { get; private set; } = "No patrols";

    internal Dictionary<string, PatrolCheckpoint> Capture()
    {
        var result = new Dictionary<string, PatrolCheckpoint>();
        foreach (var pair in _squads)
            result.Add(pair.Key, pair.Value.State.Capture(Time.time));
        return result;
    }

    internal void Restore(Dictionary<string, PatrolCheckpoint> saved)
    {
        foreach (var pair in saved)
            if (_squads.TryGetValue(pair.Key, out var squad))
            {
                Release(squad);
                foreach (var member in squad.Members)
                    member.Command = null;
                squad.State.Restore(pair.Value, Time.time);
                squad.Replan = true;
                squad.Planning = false;
                squad.NextUpdate = 0;
                squad.Navigation.Clear();
            }
    }

    internal EncounterPatrolRuntime(MapLayout layout)
    {
        EnsureInstalled();
        _layout = layout;
    }

    private static void EnsureInstalled()
    {
        if (_installed)
            return;
        var brains = BrainManager
            .CustomLayersReadOnly.Values.AsValueEnumerable()
            .Where(l => l.customLayerType.Assembly == typeof(SAINExternal).Assembly)
            .SelectMany(l => l.CustomLayerBrains)
            .Distinct()
            .ToList();
        if (brains.Count == 0)
            throw new InvalidOperationException("SAIN has not registered compatible BigBrain layers.");
        BrainManager.AddCustomLayer(
            typeof(CampaignPatrolLayer),
            brains,
            40,
            new List<WildSpawnType> { WildSpawnType.assault, WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR }
        );
        BrainManager.AddCustomLayer(
            typeof(CampaignHoldLayer),
            brains,
            40,
            new List<WildSpawnType> { WildSpawnType.assault, WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR }
        );
        var harmony = new Harmony("com.wtt.campaigns.encounter.navigation");
        harmony.Patch(
            AccessTools.Method(typeof(BotMover), nameof(BotMover.MovePlayer)),
            prefix: new HarmonyMethod(typeof(EncounterPatrolRuntime), nameof(ApplyOwnedPace))
        );
        foreach (
            var method in typeof(BotMover)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .AsValueEnumerable()
                .Where(m =>
                    m.Name
                        is "GoToPoint"
                            or "GoToByWay"
                            or "GoToPointNoWay"
                            or "CurrentStateGoToPoint"
                            or "RecalcWay"
                            or "Teleport"
                            or "Stop"
                )
        )
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(EncounterPatrolRuntime), nameof(NativeNavigation)));
        _installed = true;
    }

    // If native AI writes navigation after us, relinquish ownership immediately.
    private static void NativeNavigation(BotMover __instance, MethodBase __originalMethod)
    {
        if (_writing != __instance && Movers.TryGetValue(__instance, out var member))
        {
            member.OwnsNavigation = false;
            member.LastWriter = __originalMethod.Name;
        }
    }

    internal void Add(BotOwner bot, string squadId, string routeId)
    {
        var route =
            _layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == routeId)
            ?? throw new InvalidOperationException("The assigned patrol route is missing.");
        if (!_squads.TryGetValue(squadId, out var squad))
        {
            _squads.Add(squadId, squad = new Squad(route));
            _schedule.Add(squad);
        }
        if (squad.State.Route.Id != routeId)
            throw new InvalidOperationException("A squad cannot follow two patrol routes.");
        var member = new Member
        {
            Bot = bot,
            Mover = bot.Mover,
            Health = bot.GetPlayer.ActiveHealthController,
            Id = bot.ProfileId,
            Squad = squad,
        };
        member.DeathConfirmed = !member.Health.IsAlive;
        member.Health.DiedEvent += member.OnDeath;
        squad.Members.Add(member);
        Owners.Add(bot, member);
        Movers.Add(bot.Mover, member);
        squad.State.UpdateMembers(squad.Members.AsValueEnumerable().Select(m => m.Id).ToArray());
        squad.Replan = true;
        squad.Planning = false;
        squad.NextUpdate = 0;
        Plugin.LogInfo(
            $"AI patrol assigned: bot={bot.ProfileId}, squad={squadId}, route='{route.Name}' ({route.Id}), waypoints={route.Waypoints.Count}"
        );
    }

    internal void Tick()
    {
        if (_schedule.Count == 0)
            return;
        var suspended = 0;
        var pending = 0;
        var first = _nextSquad;
        _nextSquad = (_nextSquad + 1) % _schedule.Count;
        for (var index = 0; index < _schedule.Count; index++)
        {
            var squad = _schedule[(first + index) % _schedule.Count];
            if (squad.State.Status == PatrolRuntimeStatus.Suspended)
                suspended++;
            if (!EncounterMovementPolicy.NeedsSquadPlan(squad.State.Status, squad.Planning, squad.Replan, SquadEligible(squad)))
                continue;
            if (!squad.Planning && Time.time < squad.NextUpdate)
                continue;
            if (!UpdateSquad(squad))
                pending++;
        }
        Status =
            _squads.Count
            + " patrol squads"
            + (suspended > 0 ? " · " + suspended + " suspended" : "")
            + (pending > 0 ? " · " + pending + " awaiting navigation budget" : "");
    }

    private static bool UpdateSquad(Squad squad)
    {
        // Do not allocate/replay a planning pass when no query can make progress this frame.
        // Combat still publishes its suspension immediately without needing a path query.
        if (!EncounterNavigationBudget.CanPlan && SquadEligible(squad))
        {
            return false;
        }
        if (!squad.Planning || !SquadEligible(squad))
        {
            squad.Navigation.Clear();
            squad.Snapshots.Clear();
            foreach (var member in squad.Members)
            {
                var bot = member.Bot;
                if (!member.DeathConfirmed)
                    member.DeathConfirmed = !member.Health.IsAlive;
                var alive = !member.DeathConfirmed;
                squad.Snapshots.Add(
                    new PatrolBotSnapshot
                    {
                        BotId = member.Id,
                        Alive = alive,
                        ControlState =
                            !bot || !bot.GetPlayer ? PatrolBotControlState.Unknown
                            : Eligible(bot) ? PatrolBotControlState.Eligible
                            : PatrolBotControlState.Recovery,
                        Position =
                            bot && bot.GetPlayer
                                ? new SpatialVector
                                {
                                    X = bot.GetPlayer.Transform.position.x,
                                    Y = bot.GetPlayer.Transform.position.y,
                                    Z = bot.GetPlayer.Transform.position.z,
                                }
                                : new SpatialVector(),
                    }
                );
            }
            squad.Planning = true;
        }
        squad.Navigation.BeginPass();
        if (!squad.State.TryUpdateBudgeted(squad.Snapshots, Time.time, squad.Navigation, out var update))
        {
            return false;
        }
        squad.Planning = false;
        squad.Replan = false;
        squad.NextUpdate = Time.time + .5f;
        if (update.Rejoined)
            Plugin.LogInfo($"AI patrol rejoined: route='{squad.State.Route.Name}', waypoint={update.TargetWaypointIndex + 1}");
        foreach (var member in squad.Members)
        {
            var command = update.Commands.AsValueEnumerable().FirstOrDefault(c => c.BotId == member.Id);
            if (command != null && member.MovementWaypoint != command.WaypointIndex)
            {
                if (!update.Rejoined && command.WaypointIndex == member.ContinuationWaypoint)
                {
                    member.MovementWaypoint = command.WaypointIndex;
                    member.ArrivalCorner = member.Path.LastCorner;
                }
                else
                {
                    member.MovementWaypoint = -1;
                    member.ArrivalCorner = -1;
                }
                member.ContinuationWaypoint = -1;
                member.PassedWaypoint = false;
                member.NextMove = 0;
                member.NextSpacing = 0;
            }
            member.Command = command;
        }
        if (update.Commands.Count == 0)
            Release(squad);
        return true;
    }

    internal static bool Eligible(BotOwner bot) => EligibilityReason(bot).Length == 0;

    private static void ApplyOwnedPace(BotMover __instance)
    {
        BotOwner? bot = null;
        var run = false;
        var spacingPace = 1f;
        if (Movers.TryGetValue(__instance, out var member))
        {
            bot = member.Bot;
            if (!member.OwnsNavigation || BrainManager.GetActiveLayer(bot) is not CampaignPatrolLayer || !SquadEligible(member.Squad))
                return;
            run = member.Squad.State.Route.Pace == MapPatrolRoute.Run;
            spacingPace = member.SpacingPace;
        }
        else
        {
            // Native mover owns its BotOwner; no scene-wide search or global speed override.
            bot = EncounterHoldRuntime.Owner(__instance);
            if (!bot || !EncounterHoldRuntime.Active(bot) || BrainManager.GetActiveLayer(bot) is not CampaignHoldLayer)
                return;
        }
        var speed = EncounterMovementPolicy.Speed(run) * spacingPace;
        __instance.Sprint(false);
        bot.GetPlayer.EnableSprint(false);
        __instance.SetTargetMoveSpeed(speed);
        bot.GetPlayer.ChangeSpeed(speed - bot.GetPlayer.Speed);
    }

    private static string EligibilityReason(BotOwner bot)
    {
        if (!bot || bot.IsDead || !bot.GetPlayer || bot.GetPlayer.ActiveHealthController?.IsAlive != true)
            return "Bot not alive/ready";
        var sain = bot.GetComponent<BotComponent>();
        if (!sain || !sain.BotActive || sain.Decision == null || bot.Memory == null)
            return "SAIN or memory not ready";
        if (sain.GoalEnemy != null || bot.Memory.GoalEnemy != null || bot.Memory.IsUnderFire)
            return "Enemy or incoming fire";
        if (sain.Decision.HasDecision)
            return "SAIN decision active";
        if (!SAINExternal.CanBotQuest(bot, bot.GetPlayer.Transform.position))
            return "SAIN blocks patrol (combat/search)";
        var active = BrainManager.GetActiveLayer(bot);
        // Includes SAIN threat avoidance, recovery and extraction layers that need not have an enemy.
        return active == null ? "Brain layer not ready"
            : active is CampaignPatrolLayer || active.GetType().Assembly != typeof(SAINExternal).Assembly ? ""
            : "SAIN layer: " + active.GetType().Name;
    }

    private static bool SquadEligible(Squad squad)
    {
        foreach (var member in squad.Members)
        {
            var bot = member.Bot;
            if (member.DeathConfirmed || !member.Health.IsAlive)
                continue;
            if (!bot || !bot.GetPlayer)
                return false;
            if (bot.IsDead || bot.GetPlayer.ActiveHealthController?.IsAlive == false)
                continue;
            if (!Eligible(bot))
                return false;
        }
        return true;
    }

    internal static bool Active(BotOwner bot)
    {
        if (!Owners.TryGetValue(bot, out var member))
            return false;
        if (!SquadEligible(member.Squad))
        {
            Release(member.Squad);
            return false;
        }
        return member.Command != null || member.Squad.State.Status is PatrolRuntimeStatus.Waiting or PatrolRuntimeStatus.Moving;
    }

    internal static void Move(BotOwner bot)
    {
        if (!Owners.TryGetValue(bot, out var member) || !Active(bot) || member.Command == null)
            return;
        if (UpdateSpacing(member) || Arrive(member))
            return;
        var command = member.Command!;
        if (Time.time < member.NextMove || !EncounterNavigationBudget.Move())
        {
            FaceMovement(member);
            return;
        }
        member.NextMove = Time.time + .5f;
        EncounterPatrolDispatch.Apply(
            new[] { command },
            () => SquadEligible(member.Squad),
            current =>
            {
                var target =
                    member.PassedWaypoint && member.ContinuationWaypoint >= 0
                        ? EncounterNavigation.ToVector3(member.Squad.State.Route.Waypoints[member.ContinuationWaypoint].Position)
                        : EncounterNavigation.ToVector3(current.Target);
                // This logic is only invoked by our currently selected BigBrain layer.
                if (BrainManager.GetActiveLayer(bot) is not CampaignPatrolLayer)
                    return;
                if (
                    member.MovementWaypoint == current.WaypointIndex
                    && member.OwnsNavigation
                    && member.Path.Keep(bot, member.Destination, member.Navigation)
                )
                {
                    member.PathStatus = "Following existing path";
                    if (!member.PassedWaypoint && member.ContinuationWaypoint < 0)
                    {
                        var remaining = member.Path.RemainingCorners(bot);
                        if (remaining.Length >= 2 && TryContinuation(member, remaining, out var joined, out var next))
                            SubmitPath(member, joined, remaining.Length - 1, next);
                    }
                    return;
                }
                _writing = bot.Mover;
                try
                {
                    bot.Mover.Sprint(false);
                    if (!SquadEligible(member.Squad))
                    {
                        Release(member.Squad);
                        return;
                    }
                    bot.Mover.SetTargetMoveSpeed(current.Pace == MapPatrolRoute.Run ? 1f : .5f);
                    if (!SquadEligible(member.Squad))
                    {
                        Release(member.Squad);
                        return;
                    }
                    // Use the same live carved mesh as validation. GoToPoint enters
                    // baked cover-graph routing and can invoke its teleport recovery.
                    if (!member.Navigation.TryPatrolPath(bot.GetPlayer.Transform.position, target, out var corners, out var pathStatus))
                    {
                        member.PathStatus = pathStatus;
                        member.Squad.Replan = true;
                        member.Squad.NextUpdate = 0;
                        Release(member);
                        return;
                    }
                    if (!SquadEligible(member.Squad))
                    {
                        Release(member.Squad);
                        return;
                    }
                    if (bot.BotLay.IsLay)
                    {
                        bot.BotLay.GetUp(false);
                        if (bot.BotLay.IsLay)
                        {
                            member.PathStatus = "Waiting to stand";
                            return;
                        }
                    }
                    var arrivalCorner = member.PassedWaypoint ? -1 : corners.Length - 1;
                    var continuation = member.PassedWaypoint ? member.ContinuationWaypoint : -1;
                    if (!member.PassedWaypoint && TryContinuation(member, corners, out var combined, out var next))
                    {
                        corners = combined;
                        continuation = next;
                    }
                    if (
                        member.MovementWaypoint == current.WaypointIndex
                        && member.ContinuationWaypoint == continuation
                        && member.OwnsNavigation
                        && member.Path.TryRetain(bot, corners[corners.Length - 1], corners)
                    )
                    {
                        member.PathStatus = "Following revalidated path";
                        return;
                    }
                    SubmitPath(member, corners, arrivalCorner, continuation);
                }
                finally
                {
                    _writing = null;
                }
            },
            () => Release(member.Squad)
        );
        FaceMovement(member);
    }

    private static int NextWaypoint(Member member) =>
        EncounterMovementPolicy.Continuation(
            member.Squad.State.Route,
            member.Command!.WaypointIndex,
            member.Squad.State.Capture(Time.time).Direction
        );

    // The extra leg consumes its own query token. Deferral leaves the current
    // owned path intact, and a later movement refresh can extend it in place.
    private static bool TryContinuation(Member member, Vector3[] approach, out Vector3[] joined, out int next)
    {
        joined = approach;
        next = NextWaypoint(member);
        if (next < 0)
            return false;
        var target = EncounterNavigation.ToVector3(member.Squad.State.Route.Waypoints[next].Position);
        var points = new System.Numerics.Vector3[approach.Length];
        for (var i = 0; i < approach.Length; i++)
            points[i] = new(approach[i].x, approach[i].y, approach[i].z);
        if (!EncounterMovementPolicy.CanJoin(points, new(target.x, target.y, target.z)) || !EncounterNavigationBudget.Move())
            return false;
        if (!member.Navigation.TryPatrolPath(approach[approach.Length - 1], target, out var continuation, out _))
            return false;
        joined = EncounterMovementPolicy.JoinLegs(approach, continuation);
        return true;
    }

    private static void SubmitPath(Member member, Vector3[] corners, int arrivalCorner, int continuation)
    {
        var bot = member.Bot;
        if (!SquadEligible(member.Squad) || BrainManager.GetActiveLayer(bot) is not CampaignPatrolLayer)
            return;
        var previousWriter = _writing;
        _writing = bot.Mover;
        try
        {
            bot.WeaponManager.Stationary.StartMove();
            bot.Mover.GoToByWay(corners, EncounterMovementPolicy.PatrolReachDistance);
            member.LastRepath = member.OwnsNavigation ? member.Path.RepathReason : "Native path not owned";
            member.PathSubmissions++;
            member.Destination = corners[corners.Length - 1];
            member.Path.Submitted(bot, member.Destination);
            member.MovementWaypoint = member.Command!.WaypointIndex;
            member.ContinuationWaypoint = continuation;
            member.ArrivalCorner = arrivalCorner;
            member.PathStatus = continuation >= 0 ? "Following continuous route" : "PathComplete";
            member.LastWriter = "Campaign patrol";
            member.OwnsNavigation = true;
        }
        finally
        {
            _writing = previousWriter;
        }
    }

    private static Vector3 Heading(Member member)
    {
        var command = member.Command;
        if (command == null)
            return Vector3.zero;
        if (member.SpacingHeld && member.HeadingWaypoint == command.WaypointIndex)
            return member.Heading;
        return member.Path.Direction(
            member.Bot,
            member.OwnsNavigation ? member.Destination : EncounterNavigation.ToVector3(command.Target)
        );
    }

    private static bool UpdateSpacing(Member member)
    {
        if (Time.time < member.NextSpacing)
            return member.SpacingHeld;
        member.NextSpacing = Time.time + .1f;
        var heading = Heading(member);
        var position = member.Bot.GetPlayer.Transform.position;
        var pace = 1f;
        var passedSelf = false;
        static System.Numerics.Vector3 Numeric(Vector3 v) => new(v.x, v.y, v.z);
        foreach (var other in member.Squad.Members)
        {
            if (other == member)
            {
                passedSelf = true;
                continue;
            }
            if (other.DeathConfirmed || !other.Health.IsAlive || !other.Bot || !other.Bot.GetPlayer || other.Command == null)
                continue;
            var otherPosition = other.Bot.GetPlayer.Transform.position;
            // A follower already waiting at the waypoint must not prevent the
            // elected leader from reaching it and advancing the whole squad.
            if (
                member.Id == member.Squad.State.LeaderId
                && EncounterMovementPolicy.AtWaypoint((otherPosition - EncounterNavigation.ToVector3(member.Command!.Target)).sqrMagnitude)
            )
                continue;
            pace = Mathf.Min(
                pace,
                EncounterSquadSpacing.Pace(
                    Numeric(position),
                    Numeric(heading),
                    Numeric(otherPosition),
                    Numeric(Heading(other)),
                    !passedSelf,
                    member.SpacingHeld
                )
            );
        }
        var wasHeld = member.SpacingHeld;
        member.Heading = heading;
        member.HeadingWaypoint = member.Command!.WaypointIndex;
        member.SpacingPace = pace;
        member.SpacingHeld = pace == 0;
        if (member.SpacingHeld)
        {
            member.PathStatus = "Maintaining squad gap";
            Release(member);
        }
        else if (wasHeld)
            member.NextMove = 0;
        return member.SpacingHeld;
    }

    // Arrival is cheap and must run every action tick, before path-query quotas.
    // A zero-wait successor can replace the native path without an intervening Stop.
    private static bool Arrive(Member member)
    {
        var command = member.Command!;
        var target = EncounterNavigation.ToVector3(command.Target);
        if (member.PassedWaypoint)
        {
            var continuationTarget = EncounterNavigation.ToVector3(
                member.Squad.State.Route.Waypoints[member.ContinuationWaypoint].Position
            );
            if (EncounterMovementPolicy.AtWaypoint((member.Bot.GetPlayer.Transform.position - continuationTarget).sqrMagnitude))
            {
                member.PathStatus = "Waiting for squad progress";
                Release(member);
                return true;
            }
            return false;
        }
        if (
            !EncounterMovementPolicy.AtWaypoint((member.Bot.GetPlayer.Transform.position - target).sqrMagnitude)
            && !(member.MovementWaypoint == command.WaypointIndex && member.Path.PassedCorner(member.Mover, member.ArrivalCorner))
        )
            return false;
        var squad = member.Squad;
        if (!squad.State.AcknowledgeWaypoint(member.Id, command.WaypointIndex, Time.time))
        {
            var next = NextWaypoint(member);
            if (next < 0)
            {
                Release(member);
                return true;
            }
            // Followers may traverse one validated zero-wait leg before the
            // leader catches up. They never advance the shared route state.
            member.PassedWaypoint = true;
            if (member.ContinuationWaypoint != next)
            {
                member.MovementWaypoint = -1;
                member.ContinuationWaypoint = next;
                member.NextMove = 0;
            }
            return false;
        }
        // Invalidate any unfinished planning pass for the departed waypoint.
        squad.Replan = true;
        squad.Planning = false;
        squad.NextUpdate = 0;
        foreach (var survivor in squad.Members)
        {
            survivor.Command = null;
            survivor.NextMove = 0;
        }
        if (squad.State.Status != PatrolRuntimeStatus.Moving)
        {
            Release(squad);
            return true;
        }
        if (!UpdateSquad(squad))
        {
            // Tick will resume within the shared quota. The old path can finish
            // naturally, but no stale command may acknowledge or reissue it.
            return true;
        }
        return member.Command == null;
    }

    private static void FaceMovement(Member member)
    {
        var bot = member.Bot;
        if (
            !member.OwnsNavigation
            || BrainManager.GetActiveLayer(bot) is not CampaignPatrolLayer
            || !SquadEligible(member.Squad)
            || !member.Path.Owns(bot.Mover)
        )
            return;
        // Native steering follows the current path segment, including bends between waypoints.
        // Refresh every action tick; navigation repathing alone is throttled to twice a second.
        bot.Steering.LookToMovingDirection();
    }

    private static void Release(Squad squad)
    {
        foreach (var member in squad.Members)
        {
            member.PassedWaypoint = false;
            member.MovementWaypoint = -1;
            member.ContinuationWaypoint = -1;
            member.ArrivalCorner = -1;
            member.SpacingHeld = false;
            member.SpacingPace = 1;
            member.NextSpacing = 0;
            Release(member);
        }
    }

    private static void Release(Member member)
    {
        if (!member.OwnsNavigation)
            return;
        member.OwnsNavigation = false;
        if (!member.Bot || member.Bot.Mover == null)
            return;
        var mover = member.Bot.Mover;
        if (!member.Path.Owns(mover))
            return;
        _writing = mover;
        try
        {
            mover.Stop();
        }
        finally
        {
            _writing = null;
        }
    }

    internal static void Stop(BotOwner bot)
    {
        if (Owners.TryGetValue(bot, out var member))
        {
            member.SpacingHeld = false;
            member.SpacingPace = 1;
            member.NextSpacing = 0;
            Release(member);
        }
    }

    internal static string Describe(BotOwner bot)
    {
        if (!bot || !bot.GetPlayer)
            return "Bot unavailable";
        var active = BrainManager.GetActiveLayer(bot);
        var logic = BrainManager.GetActiveLogic(bot);
        var sain = bot.GetComponent<BotComponent>();
        var decisions =
            sain && sain.Decision != null
                ? $"{sain.Decision.CurrentCombatDecision}/{sain.Decision.CurrentSquadDecision}/{sain.Decision.CurrentSelfDecision}"
                : "unavailable";
        var native =
            $"layer={active?.GetType().Name ?? "none"}, logic={logic?.GetType().Name ?? "none"}, decisions={decisions}, position={bot.GetPlayer.Transform.position.ToString("F2")}, corner={bot.Mover.RealDestPoint.ToString("F2")}, target={bot.Mover.TargetPoint?.ToString("F2") ?? "none"}, cornerIndex={bot.Mover.ActualPathController.CurPath?.CurIndex.ToString() ?? "none"}";
        if (!Owners.TryGetValue(bot, out var member))
            return EncounterHoldRuntime.Describe(bot) + "; " + native;
        var reason = EligibilityReason(bot);
        return $"route='{member.Squad.State.Route.Name}', waypoint={member.Squad.State.TargetWaypointIndex + 1}, state={member.Squad.State.Status}/{member.Squad.State.SuspensionReason}, eligibility={(reason.Length == 0 ? "eligible" : reason)}, owns={member.OwnsNavigation}, path={member.PathStatus}, lastWriter={member.LastWriter}, submissions={member.PathSubmissions}, lastRepath={member.LastRepath}, spacing={(member.SpacingHeld ? "waiting" : member.SpacingPace.ToString("F2"))}, continuation={member.ContinuationWaypoint + 1}, passed={member.PassedWaypoint}; "
            + native;
    }

    internal void Remove(BotOwner bot)
    {
        if (!Owners.TryGetValue(bot, out var member))
            return;
        Release(member);
        member.Health.DiedEvent -= member.OnDeath;
        Owners.Remove(bot);
        Movers.Remove(member.Mover);
        // Keep a dead snapshot so the existing route does not treat this member as missing.
        member.DeathConfirmed = true;
        member.Squad.Replan = true;
        member.Squad.Planning = false;
        member.Squad.NextUpdate = 0;
    }

    internal void Reset()
    {
        foreach (var squad in _squads.Values)
        {
            squad.State.Cancel();
            Release(squad);
            foreach (var member in squad.Members)
            {
                member.Health.DiedEvent -= member.OnDeath;
                Owners.Remove(member.Bot);
                Movers.Remove(member.Mover);
            }
        }
        _squads.Clear();
        _schedule.Clear();
        _nextSquad = 0;
    }
}

public sealed class CampaignPatrolLayer(BotOwner bot, int priority) : CustomLayer(bot, priority)
{
    public override string GetName() => "Campaign patrol";

    public override bool IsActive() => EncounterPatrolRuntime.Active(BotOwner);

    public override Action GetNextAction() => new(typeof(CampaignPatrolLogic), "Authored patrol");

    public override bool IsCurrentActionEnding() => !IsActive();

    public override void Stop() => EncounterPatrolRuntime.Stop(BotOwner);
}

public sealed class CampaignPatrolLogic(BotOwner bot) : CustomLogic(bot)
{
    public override void Update(CustomLayer.ActionData data) => EncounterPatrolRuntime.Move(BotOwner);

    public override void Stop() => EncounterPatrolRuntime.Stop(BotOwner);
}
