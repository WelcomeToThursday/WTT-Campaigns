using System;
using System.Reflection;
using EFT;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using WTT.Campaigns.Client.Progression;

namespace WTT.Campaigns.Client.Patches.UI;

internal sealed class TraderSpendingTooltipPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TraderTooltip), nameof(TraderTooltip.Show));
    }

    [PatchPostfix]
    private static void Postfix(TraderTooltip __instance, Profile.TraderInfo traderInfo)
    {
        var show = traderInfo.TryGetNextLoyalty(out var next) && next.MinSalesSum > 0;
        __instance._moneySpentRequired.gameObject.SetActive(show);
        if (!show)
        {
            __instance._moneySpentMet.SetActive(false);
        }
    }
}
