using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Custom.Utils;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Session;

internal sealed class BotDifficultyFallbackPatch : ModulePatch
{
    private static readonly HashSet<string> Reported = new();

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(DifficultyManager), nameof(DifficultyManager.Get));
    }

    [PatchPrefix]
    private static void Prefix(ref WildSpawnType role)
    {
        var requested = role.ToString().ToLowerInvariant();
        if (BotDifficultyFallback.Resolve(requested, DifficultyManager.Difficulties) != requested)
        {
            if (Reported.Add(requested))
            {
                Plugin.LogInfo("WTT-Campaigns: no server difficulty settings for " + requested + "; using SPT's assault fallback.");
            }
            role = WildSpawnType.assault;
        }
    }
}
