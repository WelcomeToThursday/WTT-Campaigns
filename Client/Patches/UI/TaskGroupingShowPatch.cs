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
using WTT.Campaigns.Client.Story;

namespace WTT.Campaigns.Client.Patches.UI;

internal sealed class TaskGroupingShowPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(QuestsListView), nameof(QuestsListView.Show));
    }

    [PatchPrefix]
    private static void Prefix(QuestsListView __instance)
    {
        __instance.GetComponent<GroupedTaskList>()?.Clear();
        try
        {
            ProgressionClient.Load();
        }
        catch (Exception e)
        {
            Plugin.LogInfo("Trader progression metadata unavailable: " + e.Message);
        }
    }

    [PatchPostfix]
    private static void Postfix(QuestsListView __instance, IEftSession backendSession, QuestController questController, Trader trader)
    {
        (__instance.GetComponent<StoryQuestListHost>() ?? __instance.gameObject.AddComponent<StoryQuestListHost>()).Open(__instance);
        try
        {
            if (ProgressionClient.Metadata == null)
            {
                return;
            }
            var groups = __instance.GetComponent<GroupedTaskList>() ?? __instance.gameObject.AddComponent<GroupedTaskList>();
            groups.Initialize(__instance, questController, backendSession.Profile, trader);
        }
        catch (Exception e)
        {
            __instance.GetComponent<GroupedTaskList>()?.Clear();
            __instance.UpdateVisibility();
            Plugin.LogInfo("Trader task grouping unavailable: " + e.Message);
        }
    }
}
