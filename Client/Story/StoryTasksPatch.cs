using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Story;

internal sealed class StoryTasksPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TasksScreen), nameof(TasksScreen.Show));
    }

    [PatchPostfix]
    private static void Postfix(TasksScreen __instance)
    {
        var journal = __instance.GetComponent<StoryTasksHost>();
        if (!StoryClient.Available)
        {
            journal?.Clear();
            return;
        }
        journal ??= __instance.gameObject.AddComponent<StoryTasksHost>();
        journal.Open(__instance);
    }
}
