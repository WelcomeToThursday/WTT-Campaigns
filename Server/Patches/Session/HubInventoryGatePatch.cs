using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Profiles;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;

namespace SeasonalPerks.Server.Patches.Session;

[Injectable]
public class HubInventoryGatePatch(SeasonService seasons) : AbstractPatch
{
    private static SeasonService _seasons = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        return AccessTools.Method(typeof(ItemEventCallbacks), nameof(ItemEventCallbacks.HandleEvents));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(MongoId sessionID, out IDisposable __state)
    {
        __state = _seasons.Enter(_seasons.ResolveRoot(sessionID.ToString()));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(IDisposable __state, ref ValueTask<string> __result)
    {
        __result = Complete(__result, __state);
    }

    private static async ValueTask<string> Complete(ValueTask<string> original, IDisposable lease)
    {
        using (lease)
        {
            return await original;
        }
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(Exception? __exception, IDisposable? __state)
    {
        if (__exception != null)
        {
            __state?.Dispose();
        }
    }
}
