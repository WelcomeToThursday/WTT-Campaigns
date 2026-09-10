using System.Reflection;
using HarmonyLib;
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
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Progression;
using WTT.Campaigns.Shared.Progression;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public sealed class ProgressionStandingPatch(ProgressionService progression) : AbstractPatch
{
    private static ProgressionService _progression = null!;

    protected override MethodBase GetTargetMethod()
    {
        _progression = progression;
        return AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.TraderStandingRequirementCheck));
    }

    [PatchPrefix]
    private static bool Prefix(QuestCondition questProperties, PmcData profile, ref bool __result)
    {
        var target = questProperties.Target?.IsList == true ? questProperties.Target.List?.FirstOrDefault() : questProperties.Target?.Item;
        if (target == null || !_progression.StandingConditions.Contains(questProperties.Id.ToString()))
        {
            return true;
        }

        __result =
            profile.TradersInfo?.TryGetValue(new MongoId(target), out var trader) == true
            && TraderProgression.Compare(trader.Standing ?? 0, questProperties.Value ?? 0, questProperties.CompareMethod);
        return false;
    }
}
