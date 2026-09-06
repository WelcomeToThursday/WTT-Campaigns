using System.Reflection;
using System.Runtime.CompilerServices;
using EFT.HealthSystem;
using HarmonyLib;
using SeasonalPerks.Client.Patches.Health;
using SeasonalPerks.Shared.Effects.Consumables;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class ConsumableUsePatch : ModulePatch
{
    private static readonly ConditionalWeakTable<ActiveHealthController.MedEffect, ConsumptionReceipt> Receipts = new();

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveHealthController.MedEffect), nameof(ActiveHealthController.MedEffect.RegularUpdate));
    }

    [PatchPrefix]
    private static void Prefix(ActiveHealthController.MedEffect __instance, out float __state)
    {
        __state = __instance._foodDrink?.HpPercent ?? __instance._medKit?.HpResource ?? 0;
    }

    [PatchPostfix]
    private static void Postfix(ActiveHealthController.MedEffect __instance, float __state)
    {
        if (
            !ConsumableHealthEffects.IsLocalRaid(__instance.HealthController)
            || __instance.MedItem == null
            || !Receipts
                .GetOrCreateValue(__instance)
                .Observe(__state, __instance._foodDrink?.HpPercent ?? __instance._medKit?.HpResource ?? 0, __instance._interrupted)
        )
        {
            return;
        }

        Apply(__instance);
    }

    internal static void CompleteMedicine(ActiveHealthController.MedEffect effect)
    {
        if (
            !ConsumableHealthEffects.IsLocalRaid(effect.HealthController)
            || effect.MedItem == null
            || effect._foodDrink != null
            || effect._interrupted
            || (effect._medKit != null && effect._medKit.HpResource <= 0)
        )
        {
            return;
        }

        if (Receipts.GetOrCreateValue(effect).Observe(1, 0, false))
        {
            Apply(effect);
        }
    }

    private static void Apply(ActiveHealthController.MedEffect __instance)
    {
        foreach (
            var effect in ConsumableEffects.ForUse(
                Plugin.Effects,
                __instance.MedItem.StringTemplateId,
                count => UnityEngine.Random.Range(0, count)
            )
        )
        {
            ConsumableHealthEffects.Apply(__instance.HealthController, effect, __instance.MedItem.StringTemplateId);
        }
    }
}
