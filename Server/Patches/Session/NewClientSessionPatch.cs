using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Patches.Session;

[Injectable]
public class NewClientSessionPatch(SeasonService seasons, WTT.Campaigns.Server.Story.StoryService story) : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static WTT.Campaigns.Server.Story.StoryService _story = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _story = story;
        return AccessTools.Method(typeof(GameController), nameof(GameController.GameStart));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId)
    {
        if (Editor.EditorSessions.IsScratch(sessionId.ToString()))
            return;
        // Solo SPT starts a new client session after a crash; the previous local raid cannot resume.
        _seasons.MarkRaid(sessionId.ToString(), false).GetAwaiter().GetResult();
        _story.NewSession(sessionId.ToString()).GetAwaiter().GetResult();
    }
}
