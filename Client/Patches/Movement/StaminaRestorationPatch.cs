using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Movement;

internal class StaminaRestorationPatch(string methodName)
    : ModulePatch("SeasonalPerks." + methodName)
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(Physical), methodName);

    [PatchPostfix]
    private static void Postfix(
        Physical __instance,
        MethodBase __originalMethod,
        ref float __result
    )
    {
        if (!Plugin.SeasonalPlayer || !ReferenceEquals(Plugin.Player!.Physical, __instance))
        {
            return;
        }

        var bodyPart =
            __originalMethod.Name == nameof(Physical.GetHandsRestorationFunc) ? "arms" : "legs";
        __result *= Plugin.Effects.Multiplier("stamina_restore_body_parts_multiplicator", bodyPart);
    }
}
