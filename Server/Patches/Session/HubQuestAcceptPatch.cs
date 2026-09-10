using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Patches.Session;

[Injectable]
public class HubQuestAcceptPatch(SeasonService seasons, HubQuestService quests, EventOutputHolder output, HttpResponseUtil responses)
    : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static HubQuestService _quests = null!;
    private static EventOutputHolder _output = null!;
    private static HttpResponseUtil _responses = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _quests = quests;
        _output = output;
        _responses = responses;
        return AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(MongoId sessionID, AcceptQuestRequestData acceptedQuest, ref ItemEventRouterResponse __result)
    {
        if (_quests.Allowed(acceptedQuest.QuestId.ToString(), _seasons.CharacterSeasonId(sessionID.ToString())))
        {
            return true;
        }
        __result = _output.GetOutput(sessionID);
        _responses.AppendErrorToOutput(__result, "This task belongs to a different campaign.");
        return false;
    }
}
