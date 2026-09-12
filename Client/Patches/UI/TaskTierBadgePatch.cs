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

internal sealed class TaskTierBadgePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(QuestView), nameof(QuestView.Show));
    }

    [PatchPostfix]
    private static void Postfix(QuestView __instance, Quest quest)
    {
        if (__instance._title == null)
        {
            return;
        }
        var badge = __instance._title.transform.Find("ProgressionTierBadge")?.GetComponent<RankPanel>();
        var tier = ProgressionClient.Tier(quest.Id);
        if (badge == null && tier is >= 1 and <= 4)
        {
            var source = UnityEngine.Object.FindObjectOfType<TradingPlayerPanel>()?._currentRank;
            if (source == null)
            {
                return;
            }
            badge = UnityEngine.Object.Instantiate(source, __instance._title.transform, false);
            badge.name = "ProgressionTierBadge";
            var rect = (RectTransform)badge.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(TaskTierBadgeLayout.BadgeSize, TaskTierBadgeLayout.BadgeSize);
            rect.anchoredPosition = Vector2.zero;
            var layout =
                badge.GetComponent<UnityEngine.UI.LayoutElement>() ?? badge.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.ignoreLayout = true;
            foreach (var graphic in badge.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }
        if (badge != null)
        {
            var visible = tier is >= 1 and <= 4;
            var layout =
                __instance._title.GetComponent<TaskTierBadgeLayout>() ?? __instance._title.gameObject.AddComponent<TaskTierBadgeLayout>();
            layout.Apply(__instance._title, (RectTransform)badge.transform, visible);
            badge.gameObject.SetActive(visible);
            if (visible)
            {
                // Tasks use Roman I–IV, rather than the trader's special elite emblem.
                badge.Show(tier, 5);
            }
        }
    }
}
