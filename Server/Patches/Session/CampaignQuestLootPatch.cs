using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Server.Patches.Session;

[Injectable]
public sealed class CampaignQuestLootPatch(CampaignQuestLootService loot) : AbstractPatch
{
    private static CampaignQuestLootService _loot = null!;

    protected override MethodBase GetTargetMethod()
    {
        _loot = loot;
        return AccessTools.Method(typeof(BotGenerator), "GenerateBot");
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId, BotGenerationDetails botGenerationDetails, BotBase __result) =>
        _loot.Add(sessionId, botGenerationDetails.Role, __result);
}
