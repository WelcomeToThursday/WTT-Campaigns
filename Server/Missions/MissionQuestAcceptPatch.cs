using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Quests;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Missions;

/// <summary>Persists mission unlocks at the native quest acceptance boundary.</summary>
[Injectable]
public sealed class MissionQuestAcceptPatch(SeasonService seasons, MissionService missions) : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static MissionService _missions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _missions = missions;
        return AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(PmcData pmcData, AcceptQuestRequestData acceptedQuest, MongoId sessionID)
    {
        var id = sessionID.ToString();
        if (!_seasons.IsSeasonal(id))
            return;
        _missions.OnQuestAccepted(pmcData, _seasons.SeasonIdFor(pmcData), acceptedQuest.QuestId.ToString());
    }
}
