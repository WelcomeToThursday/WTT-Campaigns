using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SeasonalPerks.Server.Patches.Items;

[Injectable]
public class SecureSortInventoryPatch(SecureContainerRestrictions restrictions) : AbstractPatch
{
    private static SecureContainerRestrictions _restrictions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _restrictions = restrictions;
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.SortInventory));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(object[] __args)
    {
        return _restrictions.Check((PmcData)__args[0], __args[1], (MongoId)__args[2], (ItemEventRouterResponse)__args[3]);
    }
}
