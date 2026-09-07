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

internal sealed class TaskGroupingSelectPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(QuestsListView), nameof(QuestsListView.AutoSelectQuest));
    }

    [PatchPrefix]
    private static bool Prefix(QuestsListView __instance)
    {
        return __instance.GetComponent<GroupedTaskList>()?.AutoSelect() != true;
    }
}
