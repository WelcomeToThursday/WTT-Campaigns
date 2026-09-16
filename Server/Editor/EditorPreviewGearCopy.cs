using Newtonsoft.Json;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Server.Editor;

/// <summary>File-independent copying rules. No source item or profile is mutated.</summary>
public static class EditorPreviewGearCopy
{
    public static EditorPreviewGearResponse Placeholder(EditorSessionRegistry.Session session)
    {
        if (string.IsNullOrWhiteSpace(session.PreviewEquipmentId) || session.PreviewItems.Count == 0)
            throw new InvalidOperationException("Reconnect the editor to prepare its placeholder equipment.");
        return Copy(session.PreviewItems, session.PreviewEquipmentId, session.PreviewBindings);
    }

    public static EditorPreviewGearResponse Copy(
        IEnumerable<NativeItem> source,
        string equipmentId,
        IReadOnlyDictionary<string, string>? fastPanel = null
    )
    {
        var items = JsonConvert.DeserializeObject<List<NativeItem>>(JsonConvert.SerializeObject(source))!;
        var result = new EditorPreviewGearResponse();
        foreach (var root in items.Where(i => i.ParentId == equipmentId))
        {
            if (string.IsNullOrWhiteSpace(root.SlotId))
                throw new InvalidOperationException("Equipped item has no slot.");
            var owned = new HashSet<string> { root.Id };
            bool added;
            do
            {
                added = false;
                foreach (var child in items)
                    if (child.ParentId != null && owned.Contains(child.ParentId))
                        added |= owned.Add(child.Id);
            } while (added);
            var tree = items.Where(i => owned.Contains(i.Id)).ToList();
            // Stock SPT loadouts omit location for a magazine's only cartridge stack.
            // Normalize the detached copy, not the source profile or arbitrary authored assemblies.
            foreach (var cartridges in tree.Where(i => i.SlotId == "cartridges").GroupBy(i => i.ParentId))
            {
                var rounds = cartridges.ToArray();
                if (rounds.Length == 1 && rounds[0].Location == null)
                    rounds[0].Location = new NativeItemLocation(0);
            }
            var ids = tree.ToDictionary(i => i.Id, _ => Guid.NewGuid().ToString("N")[..24]);
            if (fastPanel != null)
                foreach (var binding in fastPanel)
                    if (ids.TryGetValue(binding.Value, out var copiedId))
                        result.FastPanel[binding.Key] = copiedId;
            var slot = root.SlotId;
            root.ParentId = null;
            root.SlotId = null;
            root.Location = null;
            ModelGraph.Rewrite(tree, value => ids.TryGetValue(value, out var fresh) ? fresh : value);
            result.Slots.Add(new EditorPreviewGearSlot { Slot = slot, Items = tree });
        }
        if (!result.Slots.Any(s => s.Slot == "Pockets"))
            throw new InvalidOperationException("The selected character has no pockets equipment.");
        return result;
    }
}
