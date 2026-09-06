using System.Reflection;
using HarmonyLib;
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
public class MedicalResourcePatch(ItemHelper items, InventoryHelper inventory, EventOutputHolder output) : AbstractPatch
{
    private static ItemHelper _items = null!;
    private static InventoryHelper _inventory = null!;
    private static EventOutputHolder _output = null!;

    protected override MethodBase GetTargetMethod()
    {
        _items = items;
        _inventory = inventory;
        _output = output;
        return AccessTools.Method(typeof(HealthController), nameof(HealthController.OffRaidHeal));
    }

    // Requests retain vanilla healing units. Scale only the inventory deduction, leaving
    // SPT's treatment and HP calculation in their original units.
    [PatchPrefix]
    private static bool Prefix(PmcData pmcData, OffraidHealRequestData request, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        var item = pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id == request.Item);
        if (item == null)
            return true;
        var multiplier = ItemResourceEffects.Multiplier(pmcData, item, _items);
        if (multiplier == 1f)
            return true;
        var properties = _items.GetItem(item.Template).Value?.Properties;
        var body = request.Part == null ? null : pmcData.Health?.BodyParts?.GetValueOrDefault(request.Part);
        if (properties == null || body?.Health == null || !request.Count.HasValue || request.Count < 0)
            return true;
        __result = _output.GetOutput(sessionID);
        var current = item.Upd?.MedKit?.HpResource ?? properties.MaxHpResource ?? 0;
        var units = Math.Min(request.Count.Value, Math.Floor(current / multiplier));
        var remainingUnits = units;
        // A client may have skipped an unaffordable treatment and only healed HP. Never
        // remove that injury or subtract its cost from healing merely because it exists.
        foreach (var name in body.Effects?.Keys.ToArray() ?? [])
        {
            if (Enum.TryParse<DamageEffectType>(name, out var kind) && properties.EffectsDamage?.TryGetValue(kind, out var effect) == true)
            {
                var cost = effect.Cost ?? 0;
                if (cost > remainingUnits)
                    continue;
                remainingUnits -= cost;
                body.Effects!.Remove(name);
            }
        }
        body.Health.Current = Math.Min(body.Health.Maximum ?? 0, (body.Health.Current ?? 0) + remainingUnits);
        item.Upd ??= new Upd();
        item.Upd.MedKit ??= new UpdMedKit();
        item.Upd.MedKit.HpResource = Math.Max(0, current - units * multiplier);
        if (item.Upd.MedKit.HpResource <= 0)
            _inventory.RemoveItem(pmcData, request.Item, sessionID, __result);
        return false;
    }
}
