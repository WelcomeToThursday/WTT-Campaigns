using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal sealed class EncounterHoldRuntime
{
    private sealed class Hold
    {
        internal Vector3 Anchor;
        internal BotMover Mover = null!;
        internal bool Returning;
        internal float NextMove;
        internal string Status = "Holding";
        internal readonly EncounterNavigation Navigation = new();
        internal readonly EncounterMovementPath Path = new();
    }

    private static readonly Dictionary<BotOwner, Hold> Holds = new();
    private static readonly Dictionary<BotMover, BotOwner> Movers = new();
    private readonly List<BotOwner> _owned = new();

    internal void Add(BotOwner bot, SpatialCapture spawn)
    {
        Holds.Add(bot, new Hold { Anchor = EncounterNavigation.ToVector3(spawn.Position), Mover = bot.Mover });
        Movers.Add(bot.Mover, bot);
        _owned.Add(bot);
        Plugin.LogInfo($"AI hold assigned: bot={bot.ProfileId}, spawn='{spawn.Name}'");
    }

    internal static bool Active(BotOwner bot) => Holds.ContainsKey(bot) && EncounterPatrolRuntime.Eligible(bot);

    internal static bool Owns(BotOwner bot) => Holds.ContainsKey(bot);

    internal static BotOwner? Owner(BotMover mover) => Movers.TryGetValue(mover, out var bot) ? bot : null;

    internal static void Stop(BotOwner bot)
    {
        if (Holds.TryGetValue(bot, out var hold) && hold.Returning && hold.Path.Owns(bot.Mover))
            bot.Mover.Stop();
    }

    internal static string Describe(BotOwner bot) =>
        Holds.TryGetValue(bot, out var hold)
            ? $"Hold near spawn: {hold.Status}, anchor={hold.Anchor.ToString("F2")}"
            : "No authored patrol";

    internal static void Move(BotOwner bot)
    {
        if (!Holds.TryGetValue(bot, out var hold) || !Active(bot) || BrainManager.GetActiveLayer(bot) is not CampaignHoldLayer)
            return;
        if (hold.Returning && hold.Path.Owns(bot.Mover))
            bot.Steering.LookToMovingDirection();
        if (Time.time < hold.NextMove)
            return;
        hold.NextMove = Time.time + .5f;
        var distance = (bot.GetPlayer.Transform.position - hold.Anchor).sqrMagnitude;
        hold.Returning = EncounterMovementPolicy.ReturnToAnchor(hold.Returning, distance);
        if (!hold.Returning)
        {
            bot.Mover.Stop();
            hold.Status = "Holding";
            return;
        }
        if (hold.Path.Keep(bot, hold.Anchor, hold.Navigation))
            return;
        if (!hold.Navigation.TryPatrolPath(bot.GetPlayer.Transform.position, hold.Anchor, out var corners, out var status))
        {
            bot.Mover.Stop();
            hold.Status = "Return blocked: " + status;
            return;
        }
        if (bot.BotLay.IsLay)
        {
            bot.BotLay.GetUp(false);
            if (bot.BotLay.IsLay)
                return;
        }
        bot.WeaponManager.Stationary.StartMove();
        bot.Mover.GoToByWay(corners, .5f);
        hold.Path.Submitted(bot, hold.Anchor);
        bot.Steering.LookToMovingDirection();
        hold.Status = "Returning";
    }

    internal void Remove(BotOwner bot)
    {
        Stop(bot);
        if (Holds.TryGetValue(bot, out var hold))
            Movers.Remove(hold.Mover);
        Holds.Remove(bot);
        _owned.Remove(bot);
    }

    internal void Reset()
    {
        foreach (var bot in _owned)
        {
            if (bot && BrainManager.GetActiveLayer(bot) is CampaignHoldLayer)
                bot.Mover.Stop();
            if (Holds.TryGetValue(bot, out var hold))
                Movers.Remove(hold.Mover);
            Holds.Remove(bot);
        }
        _owned.Clear();
    }
}

public sealed class CampaignHoldLayer(BotOwner bot, int priority) : CustomLayer(bot, priority)
{
    public override string GetName() => "Campaign hold";

    public override bool IsActive() => EncounterHoldRuntime.Active(BotOwner);

    public override Action GetNextAction() => new(typeof(CampaignHoldLogic), "Hold near authored spawn");

    public override bool IsCurrentActionEnding() => !IsActive();

    public override void Stop() => EncounterHoldRuntime.Stop(BotOwner);
}

public sealed class CampaignHoldLogic(BotOwner bot) : CustomLogic(bot)
{
    public override void Update(CustomLayer.ActionData data) => EncounterHoldRuntime.Move(BotOwner);
}
