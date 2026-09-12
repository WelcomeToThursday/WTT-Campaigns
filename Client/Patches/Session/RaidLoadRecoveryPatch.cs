using System.Reflection;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Client.Patches.Session;

internal sealed class RaidLoadRecoveryPatch : ModulePatch
{
    private static readonly FieldInfo RaidSettings = AccessTools.Field(typeof(TarkovApplication), "_localRaidSettings");

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameCreate));
    }

    [PatchPrefix]
    private static void Prefix(out string? __state)
    {
        __state = Plugin.SessionId;
    }

    [PatchPostfix]
    private static void Postfix(TarkovApplication __instance, string? __state, ref Task __result)
    {
        var characterId = __state;
        __result = RaidLoadRecovery.Complete(__result, () => Abort(__instance, characterId), Plugin.Error);
    }

    private static async Task Abort(TarkovApplication app, string? characterId)
    {
        var raidId = (RaidSettings.GetValue(app) as LocalRaidSettings)?.serverId;
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(raidId))
        {
            return;
        }
        var response = JsonConvert.DeserializeObject<Snapshot>(
            await RequestHandler.PostJsonAsync(
                "/wtt-campaigns/raid-abort",
                JsonConvert.SerializeObject(new Mutation { CharacterId = characterId, OperationId = raidId })
            )
        );
        if (response == null || response.Error != null)
        {
            throw new InvalidOperationException(response?.Error ?? "The server did not acknowledge the failed raid cleanup.");
        }
    }
}
