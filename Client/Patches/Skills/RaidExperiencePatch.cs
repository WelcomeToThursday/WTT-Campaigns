using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Skills;

public class RaidExperiencePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(BaseStatisticsManager),
            nameof(BaseStatisticsManager.EndStatisticsSession),
            new[] { typeof(ExitStatus), typeof(float) }
        );

    [PatchTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var field = typeof(ProfileStats).GetField(nameof(ProfileStats.ExperienceBonusMult))!;
        var sites = code.Where(i => i.StoresField(field)).ToArray();
        if (sites.Length != 1)
            throw new InvalidOperationException("Expected one raid experience bonus store in " + __originalMethod);
        foreach (var instruction in code)
        {
            if (ReferenceEquals(instruction, sites[0]))
            {
                yield return new CodeInstruction(instruction) { opcode = OpCodes.Ldarg_0, operand = null };
                yield return new CodeInstruction(
                    OpCodes.Call,
                    typeof(RaidExperiencePatch).GetMethod(nameof(Scale), BindingFlags.Static | BindingFlags.NonPublic)!
                );
                yield return new CodeInstruction(OpCodes.Stfld, field);
                continue;
            }
            yield return instruction;
        }
    }

    private static float Scale(float original, BaseStatisticsManager manager) => original * PmcExperience.Multiplier(manager.Profile);
}
