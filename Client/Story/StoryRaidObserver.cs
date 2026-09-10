using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Native;
using SeasonalPerks.Shared.Story;
using ZLinq;

namespace SeasonalPerks.Client.Story;

internal static class StoryRaidObserver
{
    private static long _sequence = DateTime.UtcNow.Ticks;

    internal static StoryRaidObservation? Capture()
    {
        if (!Plugin.InRaid || !Plugin.SeasonalPlayer || StoryClient.Current?.State?.Raid is not { Finished: false } raid)
        {
            return null;
        }

        var profile = Plugin.Player!.Profile;
        var roots = new Item[] { profile.Inventory.Equipment, profile.Inventory.QuestRaidItems }
            .AsValueEnumerable()
            .Where(static i => i != null)
            .ToArray();
        var flat = Comfort.Common.Singleton<ItemFactory>.Instance.TreeToFlatItems(roots);
        var items = JsonConvert.DeserializeObject<List<NativeQuestRewardsInfoItems>>(
            JsonConvert.SerializeObject(flat, EftJsonConverters.Converters)
        )!;
        var known = StoryClient.Current.RaidConditionIds.AsValueEnumerable().ToHashSet();
        return new StoryRaidObservation
        {
            CharacterId = profile.Id,
            RaidId = raid.Id,
            Sequence = ++_sequence,
            Level = profile.Info.Level,
            Items = items
                .AsValueEnumerable()
                .Select(static i => new StoryObservedItem
                {
                    Id = i.Id!,
                    Template = i.Template!,
                    StackCount = i.Upd?.StackObjectsCount ?? 1,
                    Data = JsonConvert.SerializeObject(i),
                })
                .ToList(),
            Counters = known
                .AsValueEnumerable()
                .ToDictionary(id => id, id => profile.TaskConditionCounters.TryGetValue(id, out var counter) ? (double)counter.Value : 0),
            CompletedConditions = profile
                .QuestsData.AsValueEnumerable()
                .SelectMany(static q => q.CompletedConditions)
                .Select(id => id.ToString())
                .Where(known.Contains)
                .ToHashSet(),
            Skills = profile
                .Skills.Skills.AsValueEnumerable()
                .Where(s => StoryClient.Current.Facts!.Skills.ContainsKey(s.Id.ToString()))
                .ToDictionary(s => s.Id.ToString(), s => (double)s.Level),
            FreeSpecialSlots = profile
                .Inventory.Equipment.AllSlots.AsValueEnumerable()
                .Count(static s => s.IsSpecial && s.ContainedItem == null),
        };
    }
}
