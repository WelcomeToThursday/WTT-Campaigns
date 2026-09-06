using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Effects.Items;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class KeyUsagePatch(Type doorType) : ModulePatch("SeasonalPerks.KeyUsage." + doorType.Name)
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.DeclaredMethod(doorType, "UnlockOperation");
    }

    [PatchTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var uses = AccessTools.Field(typeof(KeyComponent), nameof(KeyComponent.NumberOfUsages));
        var delta = AccessTools.Method(typeof(KeyUsagePatch), nameof(GetUsageDelta));
        var matches = 0;
        for (var index = 0; index < code.Count; index++)
        {
            if (
                index > 0
                && index + 2 < code.Count
                && code[index].opcode == OpCodes.Ldc_I4_1
                && code[index - 1].LoadsField(uses)
                && code[index + 1].opcode == OpCodes.Add
                && code[index + 2].StoresField(uses)
            )
            {
                matches++;
                yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(code[index]).MoveBlocksFrom(code[index]);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, delta);
                continue;
            }

            yield return code[index];
        }

        if (matches != 1)
        {
            throw new InvalidOperationException("Unsupported key unlock implementation.");
        }
    }

    private static int GetUsageDelta(KeyComponent key, Player player)
    {
        if (!Plugin.SeasonalPlayer || !ReferenceEquals(player, Plugin.Player) || key.Template.MaximumNumberOfUsage <= 0)
        {
            return 1;
        }

        var keyType = key.Item is Keycard ? "keycard" : "mechanical";
        var chance = 1f;
        foreach (var effect in Plugin.Effects.Matching("key_durability_multiplicator"))
        {
            if (RuntimeEffects.Contains(effect.KeyTypes, keyType))
            {
                chance *= KeyUsage.ConsumptionChance((float?)effect.Multiplier ?? 0);
            }
        }

        if (chance >= 1)
        {
            return 1;
        }
        return chance > 0 && UnityEngine.Random.value < chance ? 1 : 0;
    }
}
