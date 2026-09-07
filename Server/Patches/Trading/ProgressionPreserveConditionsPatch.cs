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
public sealed class ProgressionPreserveConditionsPatch(ProgressionService progression, ICloner cloner) : AbstractPatch
{
    private static ProgressionPreserveConditionsPatch _instance = null!;

    protected override MethodBase GetTargetMethod()
    {
        _instance = this;
        return AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.RemoveQuestConditionsExceptLevel));
    }

    [PatchPrefix]
    private static bool Prefix(Quest quest, ref Quest __result)
    {
        return _instance.Preserve(quest, ref __result);
    }

    private bool Preserve(Quest quest, ref Quest result)
    {
        if (!progression.Metadata.Quests.ContainsKey(quest.Id.ToString()))
        {
            return true;
        }
        result = cloner.Clone(quest)!;
        return false;
    }
}
