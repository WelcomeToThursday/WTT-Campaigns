using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Client.Authoring.Preview;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Tests;

internal static class EditorPreviewGearChecks
{
    internal static void Run()
    {
        GeneratedContainerPositions();
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
        var transported = JsonConvert.DeserializeObject<WTT.Campaigns.Shared.Authoring.EditorPreviewGearResponse>(
            JsonConvert.SerializeObject(first)
        )!;
        var copiedRounds = transported.Slots.SelectMany(s => s.Items).Single(i => i.Template == "rounds-tpl");
        Check(
            ItemStackPosition.Require(copiedRounds.Location, false) == 0,
            "The actual copied gear response supplies an explicit magazine position to the client."
        );
        var originalRounds = source.Single(i => i.Id == "rounds");
        Check(originalRounds.Location == null, "Normalizing copied ammunition leaves the original profile unchanged.");
        originalRounds.Location = new NativeItemLocation(2);
        Check(CopiedRounds(source).Location?.Slot == 2, "Explicit cartridge positions are preserved, not silently repaired.");
        originalRounds.Location = new NativeItemLocation(-1);
        RejectPosition(CopiedRounds(source).Location, "Invalid negative cartridge positions remain rejected.");
        originalRounds.Location = new NativeItemLocation(new NativeGridLocation());
        RejectPosition(CopiedRounds(source).Location, "Grid-shaped cartridge positions remain rejected.");
        originalRounds.Location = null;
        source.Add(
            new NativeItem
            {
                Id = "other-rounds",
                Template = "other-rounds-tpl",
                ParentId = "magazine",
                SlotId = "cartridges",
            }
        );
        RejectPosition(CopiedRounds(source).Location, "Multiple unpositioned stacks are not assigned an invented ammunition order.");
        source.RemoveAt(source.Count - 1);
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

    private static void GeneratedContainerPositions()
    {
        var items = new List<NativeItem>
        {
            new() { Id = "container", Template = "container-tpl" },
            new()
            {
                Id = "mag",
                Template = "mag-tpl",
                ParentId = "container",
                SlotId = "main",
                Location = new(new NativeGridLocation { X = 1, Y = 2 }),
            },
            new()
            {
                Id = "ammo",
                Template = "ammo-tpl",
                ParentId = "mag",
                SlotId = "cartridges",
                Upd = new() { StackObjectsCount = 20 },
            },
            new()
            {
                Id = "second-ammo",
                Template = "ammo-tpl",
                ParentId = "another-mag",
                SlotId = "cartridges",
                Location = new(3),
            },
        };
        GeneratedCartridgePositions.Normalize(items);
        var response = new WTT.Campaigns.Shared.Authoring.SceneContainerResponse { Contents = new() { ["placement"] = items } };
        var received = JsonConvert
            .DeserializeObject<WTT.Campaigns.Shared.Authoring.SceneContainerResponse>(JsonConvert.SerializeObject(response))!
            .Contents["placement"];
        Check(
            ItemStackPosition.Require(received[2].Location, false) == 0 && received[2].Upd!.StackObjectsCount == 20,
            "Generated magazine loot survives the container response and actual client position check"
        );
        Check(
            received[1].Location!.Grid!.X == 1 && received[3].Location!.Slot == 3,
            "Generated ammo normalization preserves grid locations and explicit cartridge ordering"
        );
        items[2].Location = null;
        items.Add(
            new()
            {
                Id = "ambiguous",
                ParentId = "mag",
                SlotId = "cartridges",
            }
        );
        GeneratedCartridgePositions.Normalize(items);
        RejectPosition(items[2].Location, "Generated magazines with multiple missing positions remain invalid");
        items.RemoveAt(items.Count - 1);
        items[2].Location = new(-1);
        GeneratedCartridgePositions.Normalize(items);
        RejectPosition(items[2].Location, "Generated negative ammunition positions remain invalid");
    }

    private static NativeItem CopiedRounds(List<NativeItem> source) =>
        EditorPreviewGearCopy.Copy(source, "equipment").Slots.SelectMany(s => s.Items).Single(i => i.Template == "rounds-tpl");

    private static void RejectPosition(NativeItemLocation? location, string message)
    {
        try
        {
            ItemStackPosition.Require(location, false);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    internal static void RunDatabase(string database)
    {
        var profiles = JObject.Parse(File.ReadAllText(Path.Combine(database, "templates", "profiles.json")));
        var kits = 0;
        var stacks = 0;
        var rawDuplicates = 0;
        foreach (var inventory in profiles.Descendants().OfType<JObject>().Where(o => o["items"] is JArray && o["equipment"] != null))
        {
            var source = inventory["items"]!.ToObject<List<NativeItem>>()!;
            // Some edition templates contain duplicate IDs repaired by native profile creation.
            // This file-only regression does not run that profile lifecycle or invent its repairs.
            if (source.Select(i => i.Id).Distinct().Count() != source.Count)
            {
                rawDuplicates++;
                continue;
            }
            var before = JsonConvert.SerializeObject(source);
            var response = EditorPreviewGearCopy.Copy(source, inventory["equipment"]!.Value<string>()!);
            var transported = JsonConvert.DeserializeObject<WTT.Campaigns.Shared.Authoring.EditorPreviewGearResponse>(
                JsonConvert.SerializeObject(response)
            )!;
            foreach (var group in transported.Slots.SelectMany(s => s.Items).Where(i => i.SlotId == "cartridges").GroupBy(i => i.ParentId))
            {
                var positions = group.Select(i => ItemStackPosition.Require(i.Location, false)).Order().ToArray();
                Check(positions.SequenceEqual(Enumerable.Range(0, positions.Length)), "Stock kit cartridge positions must be contiguous.");
                stacks += positions.Length;
            }
            Check(JsonConvert.SerializeObject(source) == before, "Stock profile data must remain untouched.");
            kits++;
        }
        Check(kits > 0 && stacks > 0, "Stock loadout regression must exercise actual kits with ammunition.");
        Console.WriteLine(
            $"Preview gear: {kits} stock loadouts and {stacks} ammunition stacks passed the server-copy/client-position roundtrip; {rawDuplicates} raw templates with duplicate IDs require native profile creation and were excluded."
        );
    }
}
