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

        internal void OnDeath(EDamageType _) => DeathConfirmed = true;

        internal Squad Squad = null!;
        internal string Id = "";
        internal PatrolMovementCommand? Command;
        internal bool OwnsNavigation;
        internal Vector3 Destination;
        internal float NextMove;
    }

    private sealed class Squad(MapPatrolRoute route)
    {
        internal readonly EncounterPatrolStateMachine State = new(route);
        internal readonly List<Member> Members = new();
        internal readonly List<PatrolBotSnapshot> Snapshots = new();
    }

    private static readonly Dictionary<BotOwner, Member> Owners = new();
    private static readonly Dictionary<BotMover, Member> Movers = new();

    [ThreadStatic]
    private static BotMover? _writing;
    private static bool _installed;
    private readonly Dictionary<string, Squad> _squads = new(StringComparer.Ordinal);
    private readonly MapLayout _layout;
    private readonly EncounterNavigation _navigation = new();
    private float _nextUpdate;
    internal string Status { get; private set; } = "No patrols";

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
        var harmony = new Harmony("com.wtt.campaigns.encounter.navigation");
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
    private static void NativeNavigation(BotMover __instance)
    {
        if (_writing != __instance && Movers.TryGetValue(__instance, out var member))
            member.OwnsNavigation = false;
    }

    internal void Add(BotOwner bot, string squadId, string routeId)
    {
        var route =
            _layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == routeId)
            ?? throw new InvalidOperationException("The assigned patrol route is missing.");
        if (!_squads.TryGetValue(squadId, out var squad))
            _squads.Add(squadId, squad = new Squad(route));
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
        squad.State.Start(squad.Members.AsValueEnumerable().Select(m => m.Id).ToArray());
    }

    internal void Tick()
    {
        if (Time.time < _nextUpdate)
            return;
        _nextUpdate = Time.time + .5f;
        var suspended = 0;
        foreach (var squad in _squads.Values)
        {
            squad.Snapshots.Clear();
            foreach (var member in squad.Members)
            {
                var bot = member.Bot;
                member.DeathConfirmed |= !member.Health.IsAlive;
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
                member.Command = null;
            }
            var update = squad.State.Update(squad.Snapshots, Time.time, _navigation);
            if (update.Status == PatrolRuntimeStatus.Suspended)
                suspended++;
            foreach (var command in update.Commands)
            {
                var member = squad.Members.AsValueEnumerable().First(m => m.Id == command.BotId);
                member.Command = command;
            }
            if (update.Commands.Count == 0)
                Release(squad);
        }
        Status = _squads.Count + " patrol squads" + (suspended > 0 ? " · " + suspended + " suspended" : "");
    }

    private static bool Eligible(BotOwner bot)
    {
        if (!bot || bot.IsDead || !bot.GetPlayer || bot.GetPlayer.ActiveHealthController?.IsAlive != true)
            return false;
        var sain = bot.GetComponent<BotComponent>();
        if (
            !sain
            || !sain.BotActive
            || sain.Decision == null
            || sain.Decision.HasDecision
            || sain.GoalEnemy != null
            || bot.Memory == null
            || bot.Memory.GoalEnemy != null
            || bot.Memory.IsUnderFire
        )
            return false;
        if (!SAINExternal.CanBotQuest(bot, bot.GetPlayer.Transform.position))
            return false;
        var active = BrainManager.GetActiveLayer(bot);
        // Includes SAIN threat avoidance, recovery and extraction layers that need not have an enemy.
        return active != null && (active is CampaignPatrolLayer || active.GetType().Assembly != typeof(SAINExternal).Assembly);
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
        if (Time.time < member.NextMove)
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
                    var result = bot.Mover.GoToPoint(target, true, .5f, false, true);
                    member.OwnsNavigation = result == NavMeshPathStatus.PathComplete;
                    member.Destination = target;
                    if (!member.OwnsNavigation)
                        member.Command = null;
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
            || (bot.Mover.RealDestPoint - member.Destination).sqrMagnitude > .01f
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
        if ((mover.RealDestPoint - member.Destination).sqrMagnitude > .01f)
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
