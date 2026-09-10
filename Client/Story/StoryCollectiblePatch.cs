using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryCollectiblePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ItemController), nameof(ItemController.RaiseAddEvent));
    }

    [PatchPostfix]
    private static void Postfix(AddItemEventArgs args)
    {
        if (args.Status == CommandStatus.Succeed && Plugin.InRaid && Plugin.SeasonalPlayer && args.OwnerId == Plugin.Player!.Profile.Id)
        {
            StoryRaidRuntime.Instance.Collect(args.Item.Id, args.Item.TemplateId);
        }
    }
}
