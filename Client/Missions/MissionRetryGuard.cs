using EFT;
using EFT.HealthSystem;
using EFT.Quests;
using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Only the explicitly attached mission player can defer native terminal defeat.</summary>
internal sealed class MissionRetryGuard : IDisposable
{
    private static MissionRetryGuard? _current;
    private static bool _installed;
    private readonly Player _player;
    private readonly Action<EDamageType> _defeated;
    private readonly HashSet<Player> _actors = new();
    internal bool Frozen { get; private set; }
    private bool _defeatRaised;

    internal MissionRetryGuard(Player player, Action<EDamageType> defeated)
    {
        if (_current != null)
            throw new InvalidOperationException("Another mission already owns defeat handling.");
        Install();
        _player = player;
        _defeated = defeated;
        _actors.Add(player);
        _current = this;
    }

    internal void Track(Player player) => _actors.Add(player);

    internal static bool RestoringActors => _current?.Frozen == true;

    internal static void TrackMissionBot(Player player) => _current?.Track(player);

    internal void Freeze()
    {
        Frozen = true;
        MissionStartupGuard.Begin(_player);
        _player.SetInventoryOpened(false);
    }

    internal void Hold()
    {
        if (Frozen)
            MissionStartupGuard.Hold(_player);
    }

    internal void Release()
    {
        _defeatRaised = false;
        MissionStartupGuard.End(_player);
        Frozen = false;
    }

    public void Dispose()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
        MissionStartupGuard.End(_player);
        _actors.Clear();
    }

    private static bool Blocks(Player? player) => player != null && _current is { Frozen: true } guard && guard._actors.Contains(player);

    internal static bool TryDefer(ActiveHealthController health, EDamageType damageType) =>
        _current != null && ReferenceEquals(health.Player, _current._player) && !Kill(health, damageType);

    [HarmonyPriority(Priority.First)]
    private static bool Kill(ActiveHealthController __instance, EDamageType damageType)
    {
        var guard = _current;
        if (guard == null || !ReferenceEquals(__instance.Player, guard._player))
            return !Blocks(__instance.Player);
        if (guard.Frozen)
            return false;
        if (!guard._defeatRaised)
        {
            guard._defeatRaised = true;
            guard.Freeze();
            guard._defeated(damageType);
        }
        return false;
    }

    private static bool Health(ActiveHealthController __instance) => !Blocks(__instance.Player);

    private static bool Damage(ActiveHealthController __instance, ref float __result)
    {
        if (!Blocks(__instance.Player))
            return true;
        __result = 0;
        return false;
    }

    private static bool BotUpdate(BotOwner __instance) =>
        __instance.BotState != EBotState.Active || __instance.Tactic?.SubTactic == null || !Blocks(__instance.GetPlayer);

    private static bool QuestUpdate(ConditionalController<Quest> __instance) =>
        _current is not { Frozen: true } guard || !ReferenceEquals(__instance.Profile, guard._player.Profile);

    private static void Changed(ActiveHealthController __instance)
    {
        var guard = _current;
        if (guard == null || !ReferenceEquals(__instance.Player, guard._player) || guard._defeatRaised)
            return;
        if (__instance.GetBodyPartHealth(EBodyPart.Head).AtMinimum || __instance.GetBodyPartHealth(EBodyPart.Chest).AtMinimum)
            Kill(__instance, EDamageType.Undefined);
    }

    private static void Install()
    {
        if (_installed)
            return;
        var harmony = new Harmony("com.wtt.campaigns.checkpoint-defeat");
        void Prefix(Type type, string native, string hook) =>
            harmony.Patch(
                AccessTools.Method(type, native) ?? throw new MissingMethodException(type.FullName, native),
                prefix: new HarmonyMethod(typeof(MissionRetryGuard), hook)
            );
        Prefix(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill), nameof(Kill));
        Prefix(typeof(ActiveHealthController), nameof(ActiveHealthController.ManualUpdate), nameof(Health));
        Prefix(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage), nameof(Damage));
        Prefix(typeof(BotOwner), nameof(BotOwner.UpdateManual), nameof(BotUpdate));
        Prefix(typeof(BotOwner), "FixedUpdate", nameof(BotUpdate));
        Prefix(typeof(ConditionalController<Quest>), "OnConditionValueChanged", nameof(QuestUpdate));
        harmony.Patch(
            AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHealth)),
            postfix: new HarmonyMethod(typeof(MissionRetryGuard), nameof(Changed))
        );
        _installed = true;
    }
}
