using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Serialization;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

internal static class MissionLootRecords
{
    internal static List<NativeItem> CopyForRun(List<NativeItem> source)
    {
        var validation = new SeasonValidationResult();
        SeasonValidator.ItemTree(source, "Mission loot", validation);
        if (!validation.CanPublish)
            throw new InvalidOperationException(validation.Issues[0].Message);
        var copy = SeasonCompiler.Copy(source);
        var identities = copy.AsValueEnumerable().ToDictionary(i => i.Id, _ => Guid.NewGuid().ToString("N").Substring(0, 24));
        ModelGraph.Rewrite(copy, value => identities.GetValueOrDefault(value) ?? value);
        var owned = copy.AsValueEnumerable().Select(i => i.Id).ToHashSet();
        var root = copy.AsValueEnumerable().Single(i => i.ParentId == null || !owned.Contains(i.ParentId));
        root.ParentId = null;
        root.SlotId = null;
        root.Location = null;
        return copy;
    }
}
