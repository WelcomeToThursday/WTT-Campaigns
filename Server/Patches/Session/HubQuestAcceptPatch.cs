using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Hub;
using SeasonalPerks.Server.Profiles;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server.Patches.Session;

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
        if (_seasons.IsSeasonal(sessionID.ToString()) || !_quests.Imported.Contains(acceptedQuest.QuestId.ToString()))
        {
            return true;
        }
        __result = _output.GetOutput(sessionID);
        _responses.AppendErrorToOutput(__result, "This task belongs to the Seasonal character.");
        return false;
    }
}
