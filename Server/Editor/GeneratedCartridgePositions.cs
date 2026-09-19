using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Server.Editor;

internal static class GeneratedCartridgePositions
{
    // Apply only to detached native-generated records, not arbitrary authored assemblies.
    internal static void Normalize(IEnumerable<NativeItem> items)
    {
        foreach (var group in items.Where(i => i.SlotId == "cartridges").GroupBy(i => i.ParentId))
        {
            var rounds = group.ToArray();
            // ItemHelper.FillMagazineWithCartridge clears location for a single stack.
            if (rounds.Length == 1 && rounds[0].Location == null)
                rounds[0].Location = new NativeItemLocation(0);
        }
    }
}
