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
                squad.State.Restore(pair.Value, Time.time);
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
            if (!squad.Planning && Time.time < squad.NextUpdate)
                continue;
            // Do not allocate/replay a planning pass when no query can make progress this frame.
            // Combat still publishes its suspension immediately without needing a path query.
            if (!EncounterNavigationBudget.CanPlan && SquadEligible(squad))
            {
                pending++;
                continue;
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
                pending++;
                continue;
            }
            squad.Planning = false;
            squad.NextUpdate = Time.time + .5f;
            if (update.Rejoined)
                Plugin.LogInfo($"AI patrol rejoined: route='{squad.State.Route.Name}', waypoint={update.TargetWaypointIndex + 1}");
            foreach (var member in squad.Members)
                member.Command = null;
            foreach (var command in update.Commands)
            {
                var member = squad.Members.AsValueEnumerable().First(m => m.Id == command.BotId);
                member.Command = command;
            }
            if (update.Commands.Count == 0)
                Release(squad);
        }
        Status =
            _squads.Count
            + " patrol squads"
            + (suspended > 0 ? " · " + suspended + " suspended" : "")
            + (pending > 0 ? " · " + pending + " awaiting navigation budget" : "");
    }

    internal static bool Eligible(BotOwner bot) => EligibilityReason(bot).Length == 0;

    private static void ApplyOwnedPace(BotMover __instance)
    {
        BotOwner? bot = null;
        var run = false;
        if (Movers.TryGetValue(__instance, out var member))
        {
            bot = member.Bot;
            if (!member.OwnsNavigation || BrainManager.GetActiveLayer(bot) is not CampaignPatrolLayer || !SquadEligible(member.Squad))
                return;
            run = member.Command?.Pace == MapPatrolRoute.Run;
        }
        else
        {
            // Native mover owns its BotOwner; no scene-wide search or global speed override.
            bot = EncounterHoldRuntime.Owner(__instance);
            if (!bot || !EncounterHoldRuntime.Active(bot) || BrainManager.GetActiveLayer(bot) is not CampaignHoldLayer)
                return;
        }
        var speed = EncounterMovementPolicy.Speed(run);
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
        return member.Command != null || member.Squad.State.Status == PatrolRuntimeStatus.Waiting;
    }

    internal static void Move(BotOwner bot)
    {
        if (!Owners.TryGetValue(bot, out var member) || !Active(bot) || member.Command == null)
            return;
        var command = member.Command;
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
                var target = EncounterNavigation.ToVector3(current.Target);
                if ((bot.GetPlayer.Transform.position - target).sqrMagnitude <= .36f)
                {
                    member.Squad.State.AcknowledgeWaypoint(member.Id, current.WaypointIndex, Time.time);
                    Release(member);
                    return;
                }
                // This logic is only invoked by our currently selected BigBrain layer.
                if (BrainManager.GetActiveLayer(bot) is not CampaignPatrolLayer)
                    return;
                if (member.OwnsNavigation && member.Path.Keep(bot, target, member.Navigation))
                {
                    member.PathStatus = "Following existing path";
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
                    bot.WeaponManager.Stationary.StartMove();
                    bot.Mover.GoToByWay(corners, .5f);
                    member.Path.Submitted(bot, target);
                    member.PathStatus = pathStatus;
                    member.LastWriter = "Campaign patrol";
                    member.OwnsNavigation = true;
                    member.Destination = target;
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
            Release(member);
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
            Release(member);
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
        return $"route='{member.Squad.State.Route.Name}', waypoint={member.Squad.State.TargetWaypointIndex + 1}, state={member.Squad.State.Status}/{member.Squad.State.SuspensionReason}, eligibility={(reason.Length == 0 ? "eligible" : reason)}, owns={member.OwnsNavigation}, path={member.PathStatus}, lastWriter={member.LastWriter}; "
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
