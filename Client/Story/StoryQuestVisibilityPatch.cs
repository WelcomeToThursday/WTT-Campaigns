using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryQuestVisibilityPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(QuestsListView), nameof(QuestsListView.UpdateSingleQuestVisibility));

    [PatchPostfix]
    private static void Postfix(QuestListItem questView)
    {
        if (!StoryQuestListHost.Allows(questView.Quest.Id))
            questView.gameObject.SetActive(false);
    }
}
