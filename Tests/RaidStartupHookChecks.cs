using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class RaidStartupHookChecks
{
    internal static void Run(string gamePath, string clientPath)
    {
        AuthoringRaidNotificationChecks.Run(gamePath, clientPath);
        using var spt = AssemblyDefinition.ReadAssembly(Path.Combine(gamePath, "BepInEx/plugins/spt/spt-custom.dll"));
        using var client = AssemblyDefinition.ReadAssembly(clientPath);
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
