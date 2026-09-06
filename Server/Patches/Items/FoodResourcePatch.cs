using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Health;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server.Patches.Items;

[Injectable]
public class FoodResourcePatch(ItemHelper items, InventoryHelper inventory, EventOutputHolder output) : AbstractPatch
{
    private static ItemHelper _items = null!;
    private static InventoryHelper _inventory = null!;
    private static EventOutputHolder _output = null!;

    protected override MethodBase GetTargetMethod()
    {
        _items = items;
        _inventory = inventory;
        _output = output;
        return AccessTools.Method(typeof(HealthController), nameof(HealthController.OffRaidEat));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(PmcData pmcData, OffraidEatRequestData request, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        var item = pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id == request.Item);
        if (item == null)
        {
            return true; // Preserve native missing-item error handling.
        }

        var multiplier = ItemResourceEffects.Multiplier(pmcData, item, _items);
        if (multiplier.Equals(1f))
        {
            return true;
        }

        var properties = _items.GetItem(item.Template).Value?.Properties;
        var maximum = properties?.MaxResource ?? 0;
        if (maximum <= 0 || !request.Count.HasValue || request.Count < 0)
        {
            return true;
        }

        __result = _output.GetOutput(sessionID);
        var current = item.Upd?.FoodDrink?.HpPercent ?? maximum;
        // Same banker's rounding as EFT's Mathf.Round for stash consumption. Single-use
        // provisions must also reach this path instead of SPT's unconditional removal.
        var remaining = Math.Max(0, current - Math.Round(request.Count.Value * (double)multiplier));
        item.Upd ??= new Upd();
        item.Upd.FoodDrink ??= new UpdFoodDrink();
        item.Upd.FoodDrink.HpPercent = remaining;
        if (remaining <= 0)
        {
            _inventory.RemoveItem(pmcData, request.Item, sessionID, __result);
        }

        // Preserve SPT's existing energy/hydration calculation using the unscaled request.
        foreach (var (factor, details) in properties!.EffectsHealth ?? [])
        {
            var value = factor switch
            {
                HealthFactor.Energy => pmcData.Health?.Energy,
                HealthFactor.Hydration => pmcData.Health?.Hydration,
                _ => null,
            };
            if (value == null)
            {
                continue;
            }

            value.Current += maximum == 1 ? details.Value : request.Count;
            value.Current = Math.Clamp(value.Current ?? 0, 0, value.Maximum ?? 0);
        }
        return false;
    }
}
