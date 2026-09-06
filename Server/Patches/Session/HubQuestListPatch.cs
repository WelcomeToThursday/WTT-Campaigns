using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Hub;
using SeasonalPerks.Server.Profiles;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SeasonalPerks.Server.Patches.Session;

[Injectable]
public class HubQuestListPatch(SeasonService seasons, HubQuestService quests) : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static HubQuestService _quests = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _quests = quests;
        return AccessTools.Method(typeof(QuestController), nameof(QuestController.GetClientQuests));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId, ref List<Quest> __result)
    {
        if (!_seasons.IsSeasonal(sessionId.ToString()))
        {
            __result = __result.Where(q => !_quests.Imported.Contains(q.Id.ToString())).ToList();
        }
    }
}
