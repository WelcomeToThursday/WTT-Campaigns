using System;
using System.Reflection;
using EFT;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using SeasonalPerks.Client.Progression;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;

namespace SeasonalPerks.Client.Patches.UI;

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
