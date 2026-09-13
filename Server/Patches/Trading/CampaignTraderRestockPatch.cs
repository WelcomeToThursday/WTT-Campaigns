using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using WTT.Campaigns.Server.Hub;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public sealed class CampaignTraderRestockPatch(HubGameplay hub) : AbstractPatch
{
    private static HubGameplay _hub = null!;

    protected override MethodBase GetTargetMethod()
    {
        _hub = hub;
        return AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.ResetExpiredTrader));
    }

    [PatchPostfix]
    private static void Postfix(Trader trader) => _hub.RestockAuthoredOffers(trader);
}
