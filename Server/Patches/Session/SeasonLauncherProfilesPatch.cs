using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Profiles;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Eft.Launcher;

namespace SeasonalPerks.Server.Patches.Session;

[Injectable]
public sealed class SeasonLauncherProfilesPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProfileController), nameof(ProfileController.GetMiniProfiles));
    }

    [PatchPostfix, UsedImplicitly]
    private static void Postfix(List<MiniProfile> __result)
    {
        __result.RemoveAll(p => SeasonProfileStorage.Contains(p.ProfileId?.ToString() ?? ""));
    }
}
