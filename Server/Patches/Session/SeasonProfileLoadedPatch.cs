using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Servers;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Patches.Session;

[Injectable]
public sealed class SeasonProfileLoadedPatch(SeasonProfileStorage storage) : AbstractPatch
{
    private static SeasonProfileStorage _storage = null!;

    protected override MethodBase GetTargetMethod()
    {
        _storage = storage;
        return AccessTools.Method(typeof(SaveServer), nameof(SaveServer.LoadAsync));
    }

    [PatchPostfix, UsedImplicitly]
    private static void Postfix(SaveServer __instance, ref Task __result)
    {
        __result = Load(__result, __instance);
    }

    private static async Task Load(Task original, SaveServer saves)
    {
        await original;
        // Complete recovery and load seasonal characters before SPT starts its native backup service.
        await _storage.Initialize(saves);
    }
}
