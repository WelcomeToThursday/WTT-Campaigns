using System.Reflection;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.UI;

namespace WTT.Campaigns.Client.Patches.UI;

internal sealed class CampaignUiInputPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InputNode), nameof(InputNode.TranslateInput));
    }

    [PatchPrefix]
    private static bool Prefix(InputNode __instance, List<ECommand> commands, ref float[]? axes, ref ECursorResult shouldLockCursor)
    {
        if (Authoring.EditorMode.Active)
            Authoring.EditorRestrictions.Filter(commands);
        var editorHome = Authoring.EditorMode.Active && !Plugin.InRaid;
        var editorBlocked = Authoring.RaidEditor.Instance && Authoring.RaidEditor.Instance!.InputBlocked;
        var storyBlocked =
            (Story.StoryVisitRuntime.Instance && Story.StoryVisitRuntime.Instance.InputBlocked)
            || (Story.StoryCinematicRuntime.Instance && Story.StoryCinematicRuntime.Instance.InputBlocked);
        if (
            !editorHome
            && !editorBlocked
            && !storyBlocked
            && (__instance is not UIInputRoot || !SeasonUi.Instance || !SeasonUi.Instance.InputBlocked)
        )
        {
            if (Story.StoryRaidRuntime.Instance)
            {
                Story.StoryRaidRuntime.Instance.ConsumeInteraction(commands);
            }
            return true;
        }
        if (Story.StoryRaidRuntime.Instance)
        {
            Story.StoryRaidRuntime.Instance.ClearCapturedInteraction();
        }
        // Block the underlying EFT UI before input is dispatched to its children.
        // Unity's input fields and buttons continue receiving their own EventSystem input.
        commands.Clear();
        shouldLockCursor = ECursorResult.ShowCursor;
        if (axes != null)
        {
            Array.Clear(axes, 0, axes.Length);
        }
        return false;
    }
}
