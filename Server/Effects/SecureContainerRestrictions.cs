using SeasonalPerks.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server.Effects;

[Injectable(InjectionType.Singleton)]
public sealed class SecureContainerRestrictions(
    TemplateTable templates,
    InventoryHelper inventory,
    EventOutputHolder outputs
)
{
    internal ItemEventRouterResponse Output(MongoId sessionId) => outputs.GetOutput(sessionId);

    internal bool Check(
        PmcData pmc,
        object request,
        MongoId sessionId,
        ItemEventRouterResponse output
    )
    {
        var effects = new RuntimeEffects(
            ServerStartup.Seasons.Catalogue,
            SeasonService.State(pmc).SeasonalPerks
        );
        if (!effects.Has(SecureContainerRules.EffectId))
            return true;
        var items = pmc.Inventory!.Items!;
        static MongoId? Id(string? value) =>
            string.IsNullOrEmpty(value) ? (MongoId?)null : new MongoId(value);
        bool Reject(
            IEnumerable<Item> source,
            MongoId? itemId,
            MongoId? parentId,
            Dictionary<MongoId, MongoId?>? replacements = null
        )
        {
            if (itemId == null || parentId == null)
                return false;
            var destination = items.ToDictionary(i => i.Id);
            var seen = new HashSet<MongoId>();
            var parent = parentId;
            bool secure = false;
            while (
                parent.HasValue
                && seen.Add(parent.Value)
                && destination.TryGetValue(parent.Value, out var container)
            )
            {
                if (
                    TemplateFilters
                        .Ancestors(templates, container.Template)
                        .Contains(SecureContainerRules.Category)
                )
                {
                    secure = true;
                    break;
                }
                parent =
                    replacements != null && replacements.TryGetValue(container.Id, out var moved)
                        ? moved
                        : Id(container.ParentId);
            }
            if (!secure)
                return false;
            var tree = source.ToArray();
            var children = tree.ToLookup(i =>
                replacements != null && replacements.TryGetValue(i.Id, out var moved)
                    ? moved
                    : Id(i.ParentId)
            );
            var queue = new Queue<MongoId>();
            queue.Enqueue(itemId.Value);
            seen.Clear();
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!seen.Add(id))
                    continue;
                var item = tree.FirstOrDefault(i => i.Id == id);
                if (
                    item == null
                    || !SecureContainerRules.Allows(
                        effects,
                        item.Template.ToString(),
                        TemplateFilters.Ancestors(templates, item.Template)
                    )
                )
                    return true;
                foreach (var child in children[id])
                    queue.Enqueue(child.Id);
            }
            return false;
        }

        bool CheckAction(InventoryBaseActionRequestData action, MongoId? item, MongoId? parent)
        {
            if (!item.HasValue)
                return false;
            var owners = inventory.GetOwnerInventoryItems(action, item.Value, sessionId);
            return ReferenceEquals(owners.To, items) && Reject(owners.From ?? [], item, parent);
        }

        var rejected = request switch
        {
            InventoryMoveRequestData r => CheckAction(r, r.Item, Id(r.To?.Id)),
            InventorySplitRequestData r => CheckAction(r, r.SplitItem, Id(r.Container?.Id)),
            InventoryMergeRequestData r => CheckAction(r, r.Item, r.With),
            InventoryTransferRequestData r => CheckAction(r, r.Item, r.With),
            InventorySwapRequestData r =>
            // Native swap uses FromOwner to select the complete inventory.
            (
                r.FromOwner == null || r.FromOwner.Type != "Profile" || Id(r.FromOwner.Id) == pmc.Id
            ) && (Reject(items, r.Item, Id(r.To?.Id)) || Reject(items, r.Item2, Id(r.To2?.Id))),
            InventorySortRequestData r => r.ChangedItems != null
                && r.ChangedItems.Any(change =>
                    Reject(
                        items,
                        change.Id,
                        Id(change.ParentId),
                        r.ChangedItems.ToDictionary(i => i.Id, i => Id(i.ParentId))
                    )
                ),
            _ => false,
        };
        if (!rejected)
            return true;
        output.Warnings ??= [];
        output.Warnings.Add(new Warning { Index = 0, ErrorMessage = SecureContainerRules.Message });
        return false;
    }
}
