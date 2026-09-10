using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Hub;

namespace WTT.Campaigns.Client.Patches.Items;

internal class HubDocumentPickupPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ItemController), nameof(ItemController.RaiseAddEvent));
    }

    [PatchPostfix]
    private static void Postfix(AddItemEventArgs args)
    {
        if (
            args.Status == CommandStatus.Succeed
            && Plugin.SeasonalPlayer
            && Plugin.InRaid
            && args.OwnerId == Plugin.Player!.Profile.Id
            && HubDocuments.IsDocument(args.Item)
        )
        {
            HubDocuments.Pickup(args.Item);
        }
    }
}
