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
public sealed class ProgressionQuestListPatch(
    ProgressionService progression,
    ProfileHelper profiles,
    SeasonService seasons,
    HubQuestService hub,
    TimeUtil time
) : AbstractPatch
{
    private static ProgressionQuestListPatch _instance = null!;

    protected override MethodBase GetTargetMethod()
    {
        _instance = this;
        return AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.GetClientQuests));
    }

    [PatchPrefix]
    private static void Prefix(MongoId sessionId)
    {
        _instance.Recalculate(sessionId);
    }

    private void Recalculate(MongoId id)
    {
        var profile = profiles.GetPmcProfile(id);
        if (profile != null)
        {
            progression.Recalculate(profile);
        }
    }

    [PatchPostfix]
    private static void Postfix(QuestHelper __instance, MongoId sessionId, ref List<Quest> __result)
    {
        __result = _instance.Preview(__instance, sessionId, __result);
    }

    private List<Quest> Preview(QuestHelper helper, MongoId id, List<Quest> quests)
    {
        var profile = profiles.GetPmcProfile(id);
        return profile == null
            ? quests
            : progression.WithPreviews(quests, profile, helper, seasons.CharacterSeasonId(id.ToString()), hub, time);
    }
}
