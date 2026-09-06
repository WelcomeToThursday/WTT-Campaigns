using EFT;
using EFT.InventoryLogic;
using SeasonalPerks.Shared.Effects.Items;

namespace SeasonalPerks.Client.Patches.Items;

internal static class SecureContainers
{
    internal static bool Reject(Item item, Item parent)
    {
        var profile = Plugin.App?.Session?.Profile;
        if (
            profile == null
            || profile.Info.Side == EPlayerSide.Savage
            || Plugin.Current?.ActiveMode != "seasonal"
            || Plugin.Current.EffectiveProfileId != profile.Id
            || !Plugin.Effects.Has(SecureContainerRules.EffectId)
            || parent.Owner == null
            || !ReferenceEquals(parent.Owner, profile.Inventory.Equipment.Owner)
        )
        {
            return false;
        }

        if (!parent.GetAllParentItemsAndSelf().OfType<MobContainer>().Any(c => c.isSecured))
        {
            return false;
        }

        var contents = item is ContainerCollection collection ? collection.GetAllItemsFromCollection() : new[] { item };
        return contents.Any(i => !SecureContainerRules.Allows(Plugin.Effects, i.StringTemplateId, Ancestors(i.Template)));
    }

    private static IEnumerable<string> Ancestors(ItemTemplate template)
    {
        for (var parent = template.Parent; parent != null; parent = parent.Parent)
        {
            yield return parent._id.ToString();
        }
    }
}
