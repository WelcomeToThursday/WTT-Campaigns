using System.Reflection;
using System.Runtime.CompilerServices;

namespace WTT.Campaigns.Tests;

internal static class RecoveryHookChecks
{
    internal static void Run(string game, string clientPath)
    {
        clientPath = Path.GetFullPath(clientPath);
        var context = new ClientAssemblyContext(game, clientPath);
        var client = context.LoadFromAssemblyPath(clientPath);
        var patch = client.GetType("WTT.Campaigns.Client.Spatial.CampaignRecoveryPatch", true)!;
        // Resolve and bind the actual recovery operation, without enabling Harmony
        // or invoking an interaction/Unity method.
        var target = (MethodInfo)
            patch
                .GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(RuntimeHelpers.GetUninitializedObject(patch), null)!;
        if (target.Name != "GetAvailableActions" || target.GetParameters()[1].ParameterType.Name != "IInteractive")
            throw new Exception("Recovery hook must target the native interactive dispatcher.");
        var recovery = (Delegate)patch.GetField("_recover", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        if (recovery.Method.Name != "StartSalvage" || recovery.Method.GetParameters()[1].ParameterType.Name != "SalvageItemTrigger")
            throw new Exception("Recovery must bind CommonLib's timed inventory transaction.");
        Console.WriteLine(
            "PASS recovery contracts: native interaction dispatcher and installed CommonLib timed transaction bind without starting the game."
        );
    }
}
