using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Hub;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Server.Progression;
using SeasonalPerks.Shared.Progression;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public sealed class ProgressionAcceptPatch(
    ProgressionService progression,
    QuestHelper helper,
    TemplateTable templates,
    EventOutputHolder output,
    HttpResponseUtil responses,
    TimeUtil time
) : AbstractPatch
{
    private static ProgressionAcceptPatch _instance = null!;

    protected override MethodBase GetTargetMethod()
    {
        _instance = this;
        return AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest));
    }

    [PatchPrefix]
    private static bool Prefix(
        PmcData pmcData,
        AcceptQuestRequestData acceptedQuest,
        MongoId sessionID,
        ref ItemEventRouterResponse __result
    )
    {
        return _instance.Accept(pmcData, acceptedQuest, sessionID, ref __result);
    }

    private bool Accept(PmcData profile, AcceptQuestRequestData request, MongoId session, ref ItemEventRouterResponse result)
    {
        if (!progression.Metadata.Quests.ContainsKey(request.QuestId.ToString()))
        {
            return true;
        }

        progression.Recalculate(profile);
        var state = profile.Quests?.FirstOrDefault(q => q.QId == request.QuestId);
        var quest = templates.Quests[request.QuestId];
        var restart = state?.Status == QuestStatusEnum.FailRestartable && quest.Restartable;
        var fresh =
            state == null
            || state.Status is QuestStatusEnum.Locked or QuestStatusEnum.AvailableForStart
            || state.Status == QuestStatusEnum.AvailableAfter && state.AvailableAfter.GetValueOrDefault() <= time.GetTimeStamp();
        if (
            (restart || fresh)
            && progression.Eligible(quest, profile, helper)
            && (restart || progression.CanStart(quest, profile, time.GetTimeStamp()))
        )
        {
            return true;
        }

        result = output.GetOutput(session);
        responses.AppendErrorToOutput(result, "This task is not available to accept. Check its trader loyalty and prerequisites.");
        return false;
    }
}
