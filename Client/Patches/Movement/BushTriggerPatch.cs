using System.Reflection;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SeasonalPerks.Client.Patches.Movement;

// Track physical occupancy independently of audio distance, audio-source availability and AI.
internal class BushTriggerPatch(string methodName) : ModulePatch("SeasonalPerks.Bush." + methodName)
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TreeInteractive), methodName);
    }

    [PatchPostfix]
    private static void Postfix(TreeInteractive __instance, Collider col, MethodBase __originalMethod)
    {
        BushOccupancy.Update(__instance, col, __originalMethod.Name == nameof(TreeInteractive.OnTriggerEnter));
    }
}
