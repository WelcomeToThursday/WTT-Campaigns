using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Movement;

internal class StaminaCapacityPatch(string methodName) : ModulePatch("SeasonalPerks." + methodName)
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
            __originalMethod.Name == nameof(Physical.GetHandsCapacityFunc) ? "arms" : "legs";
        __result = Math.Max(
            1,
            __result + Plugin.Effects.Offset("stamina_scale_body_parts", bodyPart)
        );
    }
}
