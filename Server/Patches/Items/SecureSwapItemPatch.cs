using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SeasonalPerks.Server.Patches.Items;

[Injectable]
public class SecureSwapItemPatch(SecureContainerRestrictions restrictions) : AbstractPatch
{
    private static SecureContainerRestrictions _restrictions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _restrictions = restrictions;
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.SwapItem));
    }

    [PatchPrefix]
    private static bool Prefix(PmcData pmcData, InventorySwapRequestData request, MongoId sessionId, ref ItemEventRouterResponse __result)
    {
        var output = _restrictions.Output(sessionId);
        if (_restrictions.Check(pmcData, request, sessionId, output))
            return true;
        __result = output;
        return false;
    }
}
