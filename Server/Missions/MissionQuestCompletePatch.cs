using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Quests;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Missions;

/// <summary>Latch quest-completed mission gates at the native completion boundary.</summary>
[Injectable]
public sealed class MissionQuestCompletePatch(SeasonService seasons, MissionService missions) : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static MissionService _missions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _missions = missions;
        return AccessTools.Method(typeof(QuestController), nameof(QuestController.CompleteQuest));
    }

    [PatchPostfix]
    private static void Postfix(PmcData __0, CompleteQuestRequestData __1, MongoId __2)
    {
        if (_seasons.IsSeasonal(__2.ToString()))
            _missions.OnQuestAccepted(__0, _seasons.SeasonIdFor(__0), __1.QuestId.ToString());
    }
}
