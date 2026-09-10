using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private bool AutomaticHandover(Item item)
    {
        return item.Template.ToString() is "5449016a4bdc2d6f028b456f" or "5696686a4bdc2da3298b456a" or "569668774bdc2da2298b4568"
            || templates.Items.GetValueOrDefault(item.Template)?.Properties?.QuestItem == true;
    }

    private IEnumerable<Item> HandoverItems(PmcData pmc, NativeCondition condition)
    {
        return (pmc.Inventory?.Items ?? []).Where(item =>
            MatchesItem(item, condition)
            && item.SlotId != "Dogtag"
            && item.ParentId != null
            && InPlayerInventory(pmc, item)
            && !(pmc.Inventory?.Items?.Any(child => child.ParentId == item.Id.ToString()) ?? false)
        );
    }

    private static bool InPlayerInventory(PmcData pmc, Item item)
    {
        var inventory = pmc.Inventory!;
        var byId = inventory.Items!.ToDictionary(i => i.Id.ToString());
        var seen = new HashSet<string>();
        var parent = item.ParentId;
        while (parent != null && seen.Add(parent))
        {
            if (
                parent == inventory.Stash.ToString()
                || parent == inventory.Equipment.ToString()
                || parent == inventory.QuestStashItems.ToString()
            )
            {
                return true;
            }
            parent = byId.GetValueOrDefault(parent)?.ParentId;
        }
        return false;
    }

    private bool MatchesItem(Item item, NativeCondition condition)
    {
        var targets = (condition.Target?.Values ?? []).ToHashSet();
        var template = templates.Items.GetValueOrDefault(item.Template);
        if (template == null)
        {
            return false;
        }
        var accepted = targets.Contains(item.Template.ToString());
        var parent = template.Parent;
        var seen = new HashSet<MongoId>();
        while (!accepted && seen.Add(parent))
        {
            accepted = targets.Contains(parent.ToString());
            if (!templates.Items.TryGetValue(parent, out var parentTemplate))
            {
                break;
            }
            parent = parentTemplate.Parent;
        }
        if (
            !accepted
            || (bool?)condition.OnlyFoundInRaid == true && item.Upd?.SpawnedInSession != true
            || item.Upd?.Dogtag != null && item.Upd.Dogtag.Level < ((int?)condition.DogtagLevel ?? 0)
            || item.Upd?.RecodableComponent != null
                && (item.Upd.RecodableComponent.IsEncoded ?? false) != ((bool?)condition.IsEncoded ?? false)
        )
        {
            return false;
        }
        var minimum = (double?)condition.MinDurability ?? 0;
        var maximum = (double?)condition.MaxDurability ?? 0;
        if (minimum == 0 && maximum == 0)
        {
            return true;
        }
        var properties = template.Properties!;
        var resources = new[]
        {
            (item.Upd?.Repairable?.Durability, properties.MaxDurability),
            (item.Upd?.MedKit?.HpResource, (double?)properties.MaxHpResource),
            (item.Upd?.FoodDrink?.HpPercent, (double?)properties.MaxResource),
            (item.Upd?.Resource?.Value, properties.Resource),
        };
        return resources.All(pair =>
            pair.Item1 == null || pair.Item2 > 0 && pair.Item1 >= minimum * pair.Item2 / 100 && pair.Item1 <= maximum * pair.Item2 / 100
        );
    }
}
