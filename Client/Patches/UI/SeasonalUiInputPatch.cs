using System.Reflection;
using EFT.InputSystem;
using HarmonyLib;
using SeasonalPerks.Client.UI;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.UI;

internal sealed class SeasonalUiInputPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InputNode), nameof(InputNode.TranslateInput));
    }

    [PatchPrefix]
    private static bool Prefix(InputNode __instance, List<ECommand> commands, ref float[]? axes, ref ECursorResult shouldLockCursor)
    {
        var storyBlocked =
            (Story.StoryVisitRuntime.Instance && Story.StoryVisitRuntime.Instance.InputBlocked)
            || (Story.StoryCinematicRuntime.Instance && Story.StoryCinematicRuntime.Instance.InputBlocked);
        if (!storyBlocked && (__instance is not UIInputRoot || !SeasonUi.Instance || !SeasonUi.Instance.InputBlocked))
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
