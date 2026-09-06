using System.Reflection;
using System.Reflection.Emit;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SeasonalPerks.Client.Patches.Items;

// Keep native healing, interruption, refresh, synchronization and disposal code. Change only
// resource arithmetic and the resource-limited healing/treatment checks at validated IL sites.
internal class ItemResourcePatch(Type effectType, string methodName)
    : ModulePatch("SeasonalPerks.ItemResource." + effectType.DeclaringType!.Name + "." + methodName)
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.DeclaredMethod(effectType, methodName);
    }

    internal static float Multiplier(object effect)
    {
        Item item;
        if (effect is ActiveHealthController.MedEffect active)
        {
            if (!Plugin.SeasonalPlayer || !ReferenceEquals(active.HealthController, Plugin.Player!.ActiveHealthController))
            {
                return 1f;
            }

            item = active.MedItem;
        }
        else if (effect is OfflineHealthController.MedEffect offline)
        {
            if (Plugin.Current?.ActiveMode != "seasonal" || !ReferenceEquals(offline._health._skills, Plugin.App?.Session?.Profile?.Skills))
            {
                return 1f;
            }

            item = offline.MedItem;
        }
        else
        {
            return 1f;
        }

        return Plugin.Effects.ItemResourceMultiplier(item.StringTemplateId, Ancestors(item.Template));
    }

    private static IEnumerable<string> Ancestors(ItemTemplate template)
    {
        for (var parent = template.Parent; parent != null; parent = parent.Parent)
        {
            yield return parent._id.ToString();
        }
    }

    private static float Capacity(float resource, object effect)
    {
        var multiplier = Multiplier(effect);
        if (multiplier.Equals(1f))
        {
            return resource;
        }

        var capacity = resource / multiplier;
        // Stash healing rounds HP upward; use whole affordable HP to prevent an overdraft.
        return effect is OfflineHealthController.MedEffect ? Mathf.Floor(capacity) : capacity;
    }

    private static float Cost(float cost, object effect)
    {
        return cost * Multiplier(effect);
    }

    private static float Spend(float resource, float amount, object effect)
    {
        var multiplier = Multiplier(effect);
        if (multiplier.Equals(1f))
        {
            return resource - amount;
        }

        return Mathf.Max(0f, resource - amount * multiplier);
    }

    private static float SpendFood(float resource, float amount, object effect)
    {
        var multiplier = Multiplier(effect);
        if (multiplier.Equals(1f))
        {
            return resource - amount;
        }

        var cost = amount * multiplier;
        if (effect is OfflineHealthController.MedEffect)
        {
            cost = Mathf.Round(cost);
        }

        return Mathf.Max(0f, resource - cost);
    }

    [PatchTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var med = typeof(MedKitComponent).GetField(nameof(MedKitComponent.HpResource))!;
        var food = typeof(FoodDrinkComponent).GetField(nameof(FoodDrinkComponent.HpPercent))!;
        var offline = __originalMethod.DeclaringType == typeof(OfflineHealthController.MedEffect);
        var residue = __originalMethod.Name == nameof(ActiveHealthController.MedEffect.Residue);
        var capacities = 0;
        var costs = 0;
        var spends = 0;
        var foods = 0;
        for (var i = 0; i < code.Count; i++)
        {
            var instruction = code[i];
            string helper;
            // Heal limit: Mathf.Min(healing, resource / multiplier).
            if (
                instruction.LoadsField(med)
                && i + 1 < code.Count
                && code[i + 1].operand is MethodInfo min
                && min.DeclaringType == typeof(Mathf)
                && min.Name == nameof(Mathf.Min)
            )
            {
                yield return instruction;
                helper = nameof(Capacity);
                capacities++;
            }
            // Treatment affordability only. Spending is multiplied separately below.
            else if (
                instruction.opcode == OpCodes.Conv_R4
                && i > 0
                && code[i - 1].operand is FieldInfo cost
                && cost.Name == "Cost"
                && i + 1 < code.Count
                && code[i + 1].opcode.FlowControl == FlowControl.Cond_Branch
            )
            {
                yield return instruction;
                helper = nameof(Cost);
                costs++;
            }
            else if (instruction.opcode == OpCodes.Sub && i + 1 < code.Count && code[i + 1].StoresField(med))
            {
                helper = nameof(Spend);
                spends++;
            }
            else if (
                instruction.opcode == OpCodes.Sub
                && i + 2 < code.Count
                && code[i + 1].operand is MethodInfo max
                && max.DeclaringType == typeof(Mathf)
                && max.Name == nameof(Mathf.Max)
                && code[i + 2].StoresField(food)
            )
            {
                helper = nameof(SpendFood);
                foods++;
            }
            else
            {
                yield return instruction;
                continue;
            }
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            if (instruction.opcode == OpCodes.Sub)
            {
                load.MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }

            yield return load;
            yield return new CodeInstruction(
                OpCodes.Call,
                typeof(ItemResourcePatch).GetMethod(helper, BindingFlags.Static | BindingFlags.NonPublic)
            );
        }
        if (
            capacities != (residue ? 0 : 1)
            || costs != (residue || offline ? 1 : 0)
            || spends != (offline ? 2 : 1)
            || foods != (residue ? 0 : 1)
        )
        {
            throw new InvalidOperationException("Unsupported item-resource implementation: " + __originalMethod);
        }
    }
}
