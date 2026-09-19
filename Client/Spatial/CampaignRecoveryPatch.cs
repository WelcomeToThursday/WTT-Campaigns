using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Story;
using ZLinq;

namespace WTT.Campaigns.Client.Spatial;

// Recovery grants inventory items through CommonLib's existing timed transaction.
// It is not a one-shot quest counter: losing the item must permit another attempt.
internal sealed class CampaignRecoveryPatch : ModulePatch
{
    private static Action<GamePlayerOwner, SalvageItemTrigger, InventoryController> _recover = null!;

    protected override MethodBase GetTargetMethod()
    {
        var provider = typeof(SalvageItemTrigger).Assembly.GetType("WTTClientCommonLib.Patches.GetActionsPatch", true)!;
        var operation =
            provider.GetMethod(
                "StartSalvage",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                [typeof(GamePlayerOwner), typeof(SalvageItemTrigger), typeof(InventoryController)],
                null
            ) ?? throw new MissingMethodException("Installed CommonLib lacks the timed recovery contract.");
        _recover =
            (Action<GamePlayerOwner, SalvageItemTrigger, InventoryController>)
                operation.CreateDelegate(typeof(Action<GamePlayerOwner, SalvageItemTrigger, InventoryController>));
        return typeof(InteractionContextHelper).GetMethod("GetAvailableActions", [typeof(GamePlayerOwner), typeof(IInteractive)])!;
    }

    [PatchPostfix]
    private static void Postfix(object[] __args, ref AvailableInteractionState? __result)
    {
        if (
            !Plugin.InRaid
            || Plugin.Busy
            || __args[0] is not GamePlayerOwner owner
            || owner.Player != Plugin.Player
            || __args[1] is not SalvageItemTrigger trigger
        )
            return;
        var zone = trigger.GetComponentInParent<NativeZoneBridge>()?.Definition;
        if (zone == null || !zone.Salvage.Recovery || zone.Id != trigger.Id)
            return;
        __result = null;
        if (
            zone.RequiredQuestId.Length == 0
            || !NativeZoneBridge.QuestActive(zone.RequiredQuestId)
            || owner.Player.InventoryController == null
        )
            return;
        var items = owner.Player.Profile.Inventory.AllRealPlayerItems;
        if (zone.Salvage.Rewards.AsValueEnumerable().All(r => items.AsValueEnumerable().Count(i => i.TemplateId == r.ItemTpl) >= r.Count))
            return;
        __result = new AvailableInteractionState
        {
            Actions = new List<InteractionAction>
            {
                new() { Name = zone.Name, Action = () => _recover(owner, trigger, owner.Player.InventoryController) },
            },
        };
    }
}
