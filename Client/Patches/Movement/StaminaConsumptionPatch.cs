using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SPT.Reflection.Patching;
using ZLinq;

namespace WTT.Campaigns.Client.Patches.Movement;

internal class StaminaConsumptionPatch(string methodName) : ModulePatch("WTT.Campaigns." + methodName)
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Stamina), methodName);
    }

    [PatchTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.AsValueEnumerable().ToList();
        var scale = AccessTools.Method(typeof(StaminaConsumptionPatch), nameof(ScaleConsumption));
        var matched = 0;
        for (var index = 0; index < code.Count; index++)
        {
            yield return code[index];
            if (
                index == 0
                || code[index - 1].operand is not FieldInfo field
                || field.DeclaringType != typeof(Physical.Consumption)
                || field.Name != "Delta"
                || code[index].operand is not MethodInfo getter
                || getter.ReturnType != typeof(float)
            )
            {
                continue;
            }

            matched++;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, scale);
        }

        var expected = __originalMethod.Name == nameof(Stamina.Consume) ? 1 : 2;
        if (matched != expected)
        {
            throw new InvalidOperationException("Unsupported stamina implementation: " + __originalMethod.Name);
        }
    }

    private static float ScaleConsumption(float value, Stamina stamina)
    {
        if (!Plugin.SeasonalPlayer || value <= 0)
        {
            return value;
        }

        var physical = Plugin.Player!.Physical;
        var bodyPart =
            ReferenceEquals(stamina, physical.Stamina) ? "legs"
            : ReferenceEquals(stamina, physical.HandsStamina) ? "arms"
            : null;

        return bodyPart == null ? value : value * Plugin.Effects.Multiplier("stamina_consumption_body_parts_multiplicator", bodyPart);
    }
}
