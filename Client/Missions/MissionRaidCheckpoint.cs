using Comfort.Common;
using EFT;
using WTT.Campaigns.Client.Encounters;

namespace WTT.Campaigns.Client.Missions;

/// <summary>A complete raid-local ownership unit; callers never release the guard after a failed stage.</summary>
internal sealed class MissionRaidCheckpoint
{
    private readonly MissionInventorySnapshot _inventory;
    private readonly MissionActorSnapshot _player;
    private readonly MissionWorldSnapshot _world;
    private readonly MissionAccountingSnapshot _accounting;
    private readonly MissionAudioSnapshot _audio;
    private readonly string _heldItem;
    private readonly MissionTimeSnapshot _time;
    internal EncounterPreviewRuntime.Checkpoint Encounters { get; }
    internal string Id { get; }

    internal MissionRaidCheckpoint(string id, Player player, EncounterPreviewRuntime encounters)
    {
        Id = id;
        _time = new MissionTimeSnapshot();
        _heldItem = player.HandsController?.Item?.Id.ToString() ?? "";
        Encounters = encounters.Capture();
        _inventory = new(player);
        _player = new(player);
        _world = new(Singleton<GameWorld>.Instance, player);
        _accounting = new(player);
        _audio = new(player);
    }

    internal async Task ClearAsync(Player player, CancellationToken token)
    {
        await MissionInventorySnapshot.EmptyHands(player, token);
        _world.Clear();
        _inventory.Clear(player);
    }

    internal async Task RestoreWorldAsync(Player player, MissionRetryGuard guard, CancellationToken token)
    {
        _inventory.Restore(player);
        _player.Restore(player);
        guard.Freeze();
        await _world.RestoreAsync(token);
        await MissionInventorySnapshot.RestoreHands(player, _heldItem, token);
    }

    internal void RestoreAccounting(Player player) => _accounting.Restore(player);

    internal void RestoreTime() => _time.Restore();

    internal Task RestoreAudioAsync(CancellationToken token) => _audio.RestoreAsync(token);
}
