using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class RaidStartupHookChecks
{
    internal static void Run(string gamePath, string clientPath)
    {
        AuthoringRaidNotificationChecks.Run(gamePath, clientPath);
        using var spt = AssemblyDefinition.ReadAssembly(Path.Combine(gamePath, "BepInEx/plugins/spt/spt-custom.dll"));
        using var client = AssemblyDefinition.ReadAssembly(clientPath);
        using var native = AssemblyDefinition.ReadAssembly(Path.Combine(gamePath, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
        var headers = native.MainModule.GetType("HTTPTransportManager").Methods.Single(m => m.Name == "CreateHeadersForRequest");
        Require(
            headers.IsPublic
                && headers.Parameters.Count == 1
                && headers.Parameters[0].Name == "bRequest"
                && headers.Parameters[0].ParameterType.FullName == "BackendRequestParams"
                && headers.ReturnType.FullName == "System.Collections.Generic.Dictionary`2<System.String,System.String>",
            "Native mission marker targets the installed EFT header builder signature"
        );
        var missionPatch = client.MainModule.GetType("WTT.Campaigns.Client.Patches.Session.NativeMissionLaunchPatch");
        var missionPostfix = missionPatch.Methods.Single(m => m.Name == "Postfix");
        Require(
            missionPostfix.CustomAttributes.Any(a => a.AttributeType.Name == "PatchPostfixAttribute")
                && missionPostfix.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "BackendMethod")
                && missionPostfix.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_PendingRunId")
                && missionPostfix.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ApplyNativeMissionMarker"),
            "Native header patch passes the actual request route and pending run to the tested marker policy"
        );
        var manager = spt.MainModule.GetType("SPT.Custom.Utils.DifficultyManager");
        var get = manager.Methods.Single(method => method.Name == "Get");
        Require(get.IsPublic && get.IsStatic && get.ReturnType.FullName == "System.String", "SPT difficulty lookup is public and static");
        Require(
            get.Parameters.Any(parameter => parameter.Name == "role" && parameter.ParameterType.FullName == "EFT.WildSpawnType"),
            "Difficulty prefix matches SPT's role parameter"
        );
        var cache = manager.Properties.Single(property => property.Name == "Difficulties");
        Require(cache.GetMethod.IsPublic && cache.GetMethod.IsStatic, "SPT exposes the loaded difficulty cache");
        Require(
            cache.PropertyType is GenericInstanceType dictionary
                && dictionary.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
                && dictionary.GenericArguments[0].FullName == "System.String",
            "SPT difficulty cache has string role keys"
        );
        var registration = client
            .MainModule.GetType("WTT.Campaigns.Client.Patches.PatchRegistration")
            .Methods.Single(method => method.Name == "EnableSession");
        Require(
            registration.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == missionPatch.FullName),
            "Native mission header patch is enabled at client startup"
        );
        var runtime = client.MainModule.GetType("WTT.Campaigns.Client.Missions.MissionRaidRuntime");
        var missionUi = client.MainModule.GetType("WTT.Campaigns.Client.Missions.MissionUi");
        var recovery = missionUi
            .NestedTypes.Single(t => t.Name.StartsWith("<RecoverFailedLaunch>"))
            .Methods.Single(m => m.Name == "MoveNext");
        var recoveryCalls = recovery.Body.Instructions.Where(i => i.Operand is MethodReference).ToArray();
        Require(
            recoveryCalls.Any(i => ((MethodReference)i.Operand).Name == "AbortPendingRaid")
                && recoveryCalls.Any(i => ((MethodReference)i.Operand).Name == "get_InRaid")
                && recoveryCalls.Single(i => ((MethodReference)i.Operand).Name == "Yield").Offset
                    < recoveryCalls.Single(i => ((MethodReference)i.Operand).Name == "ComebackToMainMenu").Offset,
            "Failed mission loading unwinds cancellation and restores the native menu when no raid owns the return path"
        );
        foreach (var entry in new[] { "Deploy", "Resume" })
            Require(
                missionUi
                    .NestedTypes.Single(t => t.Name.StartsWith("<" + entry + ">"))
                    .Methods.Single(m => m.Name == "MoveNext")
                    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RecoverFailedLaunch"),
                entry + " recovers native loading before reopening the missions screen"
            );
        var stop = runtime.NestedTypes.Single(t => t.Name.StartsWith("<StopFailedStartupAsync>")).Methods.Single(m => m.Name == "MoveNext");
        Require(
            stop.Body.Instructions.Any(i =>
                i.Operand is MethodReference m && m.Name == "get_Status" && m.DeclaringType.FullName == "EFT.AbstractGame"
            )
                && stop.Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.Name == "Yield" && m.DeclaringType.FullName == "Cysharp.Threading.Tasks.UniTask"
                )
                && stop.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Stop"),
            "Failed mission startup waits on native game readiness through the Unity player loop before stopping"
        );
        foreach (var name in new[] { "BotDifficultyFallbackPatch", "RaidLoadRecoveryPatch" })
        {
            var patch = client.MainModule.GetType("WTT.Campaigns.Client.Patches.Session." + name);
            Require(
                registration.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference call && call.DeclaringType.FullName == patch.FullName
                ),
                name + " is registered"
            );
            Require(
                patch.Methods.Any(method =>
                    method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "PatchPrefixAttribute")
                ),
                name + " has a Harmony prefix"
            );
            if (name == "RaidLoadRecoveryPatch")
            {
                var prefix = patch.Methods.Single(method => method.Name == "Prefix");
                var postfix = patch.Methods.Single(method => method.Name == "Postfix");
                Require(
                    prefix.Body.Instructions.Any(instruction =>
                        instruction.Operand is MethodReference call
                        && call.Name == "get_MapLoadActive"
                        && call.DeclaringType.FullName == "WTT.Campaigns.Client.Authoring.EditorMode"
                    ),
                    "Raid load recovery captures editor map-load state before native startup"
                );
                Require(
                    postfix.Body.Instructions.Any(instruction =>
                        instruction.Operand is MethodReference call
                        && call.Name == "SelectCleanup"
                        && call.DeclaringType.FullName == "WTT.Campaigns.Client.Patches.Session.RaidLoadRecovery"
                    ),
                    "Raid load recovery preserves gameplay abort while routing editor failures to editor unload"
                );
            }
        }
        Console.WriteLine("Raid startup: installed SPT cache and compiled client hook contracts verified offline.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
