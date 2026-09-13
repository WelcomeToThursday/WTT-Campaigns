using SPTarkov.Server.Core.Models.Eft.Common;

namespace WTT.Campaigns.Server.Editor;

public static class EditorScratchInventory
{
    public static void Prepare(PmcData pmc)
    {
        var inventory = pmc.Inventory ?? throw new InvalidOperationException("Editor template has no inventory.");
        // Native player initialization requires the pockets container even for an empty traversal character.
        var pockets =
            inventory.Items?.SingleOrDefault(i => i.SlotId == "Pockets" && i.ParentId == inventory.Equipment.ToString())
            ?? throw new InvalidOperationException("Editor template has no native pockets container.");
        inventory.Items = inventory.Items!.Where(i => string.IsNullOrEmpty(i.ParentId) || i.Id == pockets.Id).ToList();
        inventory.FastPanel = new();
        pmc.InsuredItems = new();
        pmc.Quests = new();
    }
}
