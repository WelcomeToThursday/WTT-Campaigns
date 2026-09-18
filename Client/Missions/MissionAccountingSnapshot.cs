using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Rewinds local quest connections and native kill, XP, skill and loot counters in place.</summary>
internal sealed class MissionAccountingSnapshot
{
    private readonly Player _player;
    private readonly CheckpointObjectState _state;
    private readonly string _expected;

    internal MissionAccountingSnapshot(Player player)
    {
        _player = player;
        var roots = new List<object>
        {
            player.Profile.Info, player.Profile.Stats, player.Profile.Skills, player.Profile.QuestsData,
            player.Profile.TaskConditionCounters, player.StatisticsManager,
            player, player.InventoryController, player.SearchController,
        };
        if (player.QuestController != null) roots.Add(player.QuestController);
        _state = new CheckpointObjectState(roots, Owns, Member);
        _expected = Describe(player);
    }

    private bool Member(object target, FieldInfo field)
    {
        if (ReferenceEquals(target, _player) || ReferenceEquals(target, _player.InventoryController) || ReferenceEquals(target, _player.SearchController))
            return typeof(Delegate).IsAssignableFrom(field.FieldType);
        // The recurring native statistics coroutine remains alive; its next iteration reads restored counters.
        return field.FieldType != typeof(UnityEngine.Coroutine);
    }

    private static bool Owns(object value)
    {
        if (value.GetType().Name.Contains("Disposable") && value.GetType().Assembly == typeof(Player).Assembly) return true;
        return MissionActorSnapshot.OwnsHealthState(value);
    }

    internal void Restore(Player player)
    {
        if (!ReferenceEquals(player, _player)) throw new InvalidOperationException("Checkpoint accounting belongs to another player.");
        _state.Restore();
        if (Describe(player) != _expected)
            throw new InvalidOperationException("Native quest, skill or reward accounting did not match the checkpoint.");
    }

    private static string Describe(Player player)
    {
        var profile = new ProfileDescriptor(player.Profile, player.SearchController);
        return JsonConvert.SerializeObject(new { profile.Info, profile.Skills, profile.Stats, profile.QuestsData, profile.TaskConditionCounters }, EftJsonConverters.Converters);
    }
}
