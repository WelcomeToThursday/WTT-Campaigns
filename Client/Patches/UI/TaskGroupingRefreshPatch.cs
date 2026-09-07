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

internal sealed class TaskGroupingRefreshPatch(string method) : ModulePatch("SeasonalPerks.TaskGrouping." + method)
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(QuestsListView), method);
    }

    [PatchPostfix]
    private static void Postfix(QuestsListView __instance)
    {
        try
        {
            __instance.GetComponent<GroupedTaskList>()?.Refresh();
        }
        catch (Exception e)
        {
            __instance.GetComponent<GroupedTaskList>()?.Clear();
            __instance.UpdateVisibility();
            Plugin.LogInfo("Trader task grouping disabled after refresh error: " + e.Message);
        }
    }
}
