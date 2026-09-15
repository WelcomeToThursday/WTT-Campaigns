using Newtonsoft.Json;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Tests;

internal static class EditorPreviewGearChecks
{
    internal static void Run()
    {
        var source = new List<NativeItem>
        {
            new() { Id = "equipment", Template = "equipment-tpl" },
            new()
            {
                Id = "pockets",
                Template = "pockets-tpl",
                ParentId = "equipment",
                SlotId = "Pockets",
            },
            new()
            {
                Id = "rig",
                Template = "rig-tpl",
                ParentId = "equipment",
                SlotId = "TacticalVest",
            },
            new()
            {
                Id = "magazine",
                Template = "magazine-tpl",
                ParentId = "rig",
                SlotId = "main",
            },
            new()
            {
                Id = "rounds",
                Template = "rounds-tpl",
                ParentId = "magazine",
                SlotId = "cartridges",
            },
            new() { Id = "stash", Template = "stash-tpl" },
            new()
            {
                Id = "stash-weapon",
                Template = "weapon-tpl",
                ParentId = "stash",
                SlotId = "main",
            },
        };
        var before = JsonConvert.SerializeObject(source);
        var first = EditorPreviewGearCopy.Copy(
            source,
            "equipment",
            new Dictionary<string, string> { ["4"] = "magazine", ["5"] = "stash-weapon" }
        );
        var second = EditorPreviewGearCopy.Copy(source, "equipment");
        Check(JsonConvert.SerializeObject(source) == before, "Copying preview equipment never mutates source records.");
        Check(first.Slots.Count == 2 && first.Slots.Sum(s => s.Items.Count) == 4, "Copy only equipped item trees, excluding stash.");
        var tree = first.Slots.Single(s => s.Slot == "TacticalVest").Items;
        var rig = tree.Single(i => i.Template == "rig-tpl");
        var mag = tree.Single(i => i.Template == "magazine-tpl");
        Check(
            first.FastPanel.Count == 1 && first.FastPanel["4"] == mag.Id,
            "Quickslots reference copied equipped items and exclude source stash items."
        );
        Check(
            rig.ParentId == null && rig.SlotId == null && mag.ParentId == rig.Id,
            "Detach slot roots while preserving remapped nested parents."
        );
        Check(tree.Single(i => i.Template == "rounds-tpl").ParentId == mag.Id, "Nested ammunition remains in its magazine.");
        var ids = first.Slots.SelectMany(s => s.Items).Select(i => i.Id).ToHashSet();
        Check(!second.Slots.SelectMany(s => s.Items).Any(i => ids.Contains(i.Id)), "Each preview receives new item identities.");
        tree[0].Template = "changed";
        Check(JsonConvert.SerializeObject(source) == before, "Returned item trees share no mutable records with source.");
        try
        {
            EditorPreviewGearCopy.Copy(source.Where(i => i.Id != "pockets"), "equipment");
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Preview equipment must retain a pockets container.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
