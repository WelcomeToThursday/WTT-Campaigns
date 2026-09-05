using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SeasonalPerks.Tests;

internal static class CompatibilityChecks
{
    internal static void Run(string assemblyPath, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var types = assembly.MainModule.GetTypes().ToDictionary(type => type.FullName);
        MethodDefinition Method(string type, string method)
        {
            var matches = types[type].Methods.Where(value => value.Name == method).ToArray();
            check(matches.Length == 1, type + "." + method + " is unambiguous");
            return matches.Single();
        }

        foreach (var screen in new[] { "EFT.UI.MenuScreen", "EFT.UI.SkillsAndMasteringScreen" })
        {
            check(
                types[screen]
                    .Methods.Count(method =>
                        method.Name == "Show"
                        && method.Parameters.FirstOrDefault()?.ParameterType.FullName
                            == "EFT.Profile"
                    ) == 1,
                screen + " has one profile Show entry point"
            );
        }

        foreach (
            var door in new[]
            {
                "EFT.Interactive.WorldInteractiveObject",
                "EFT.Interactive.KeycardDoor",
            }
        )
        {
            var method = Method(door, "UnlockOperation");
            check(
                !method.IsStatic
                    && method.Parameters[0].ParameterType.Name == "KeyComponent"
                    && method.Parameters[1].ParameterType.FullName == "EFT.Player",
                door + " unlock arguments"
            );
            var code = method.Body.Instructions;
            var matches = 0;
            for (var i = 1; i + 2 < code.Count; i++)
            {
                if (
                    code[i].OpCode == OpCodes.Ldc_I4_1
                    && code[i - 1].OpCode == OpCodes.Ldfld
                    && code[i - 1].Operand is FieldReference field
                    && field.Name == "NumberOfUsages"
                    && code[i + 1].OpCode == OpCodes.Add
                    && code[i + 2].OpCode == OpCodes.Stfld
                    && code[i + 2].Operand is FieldReference stored
                    && stored.FullName == field.FullName
                )
                {
                    matches++;
                }
            }
            check(matches == 1, door + " has one key consumption increment");
        }

        foreach (var methodName in new[] { "Consume", "Process" })
        {
            var method = Method("Stamina", methodName);
            var code = method.Body.Instructions;
            var matches = 0;
            for (var i = 1; i < code.Count; i++)
            {
                if (
                    code[i - 1].Operand is FieldReference field
                    && field.DeclaringType.FullName == "Physical/Consumption"
                    && field.Name == "Delta"
                    && code[i].Operand is MethodReference getter
                    && (
                        getter.ReturnType.FullName == "System.Single"
                        || getter.ReturnType is GenericParameter
                    )
                )
                {
                    matches++;
                }
            }
            check(
                matches == (methodName == "Consume" ? 1 : 2),
                methodName + " consumption patch sites"
            );
        }

        Method("EFT.TarkovApplication", "CreateBackend");
        Method("EFT.TarkovApplication", "PrepareGame");
        Method("EFT.HealthSystem.ActiveHealthController", "ApplyDamage");
        Method("EFT.HealthSystem.ActiveHealthController/Existence", "GetEnergyDamage");
        Method("EFT.HealthSystem.ActiveHealthController/Existence", "GetHydrationDamage");
        Method("EFT.HealthSystem.ActiveHealthController/Wound", "get_DefaultWorkTime");
        Method("EFT.HealthSystem.EffectsSettings/ProbabilitySetting", "Try");
        Method("EFT.BaseSkill", "SetCurrent");
        Method("EFT.MovementContext", "get_StateSprintSpeedLimit");
        foreach (
            var method in new[]
            {
                "GetStaminaCapacityFunc",
                "GetHandsCapacityFunc",
                "GetStaminaRestorationFunc",
                "GetHandsRestorationFunc",
            }
        )
        {
            Method("Physical", method);
        }
        check(
            types["EFT.TarkovApplication"].Fields.Any(field => field.Name == "_cachedPhpSessionId"),
            "Backend session identity field exists"
        );
    }
}
