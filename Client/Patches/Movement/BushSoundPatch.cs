using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace WTT.Campaigns.Client.Patches.Movement;

internal class BushSoundPatch(string methodName) : ModulePatch("WTT.Campaigns.BushSound." + methodName)
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TreeInteractive), methodName);
    }

    private static float ScaleForCollider(float value, Collider collider)
    {
        return Plugin.SeasonalPlayer && ReferenceEquals(Singleton<GameWorld>.Instance.GetPlayerByCollider(collider), Plugin.Player)
            ? value * Plugin.Effects.BushNoiseMultiplier
            : value;
    }

    private static float ScaleForBridge(float value, IObserverToPlayerBridge player)
    {
        return Plugin.SeasonalPlayer && ReferenceEquals(player.iPlayer, Plugin.Player) ? value * Plugin.Effects.BushNoiseMultiplier : value;
    }

    [PatchTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var playback = __originalMethod.Name == nameof(TreeInteractive.PlaySoundBank);
        var rolloff = typeof(SoundBank).GetField(nameof(SoundBank.Rolloff))!;
        var randomVolume = typeof(SoundBank).GetProperty(nameof(SoundBank.RandomVolume))!.GetMethod!;
        var scale = typeof(BushSoundPatch).GetMethod(
            playback ? nameof(ScaleForBridge) : nameof(ScaleForCollider),
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        int radii = 0,
            volumes = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            bool radius = instruction.LoadsField(rolloff);
            bool volume = instruction.Calls(randomVolume);
            if (!radius && !volume)
            {
                continue;
            }

            if (radius)
            {
                radii++;
            }

            if (volume)
            {
                volumes++;
            }

            yield return new CodeInstruction(playback ? OpCodes.Ldarg_3 : OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call, scale);
        }
        if (radii != 1 || volumes != (playback ? 1 : 0))
        {
            throw new InvalidOperationException("Unexpected bush sound patch sites: " + __originalMethod);
        }
    }
}
