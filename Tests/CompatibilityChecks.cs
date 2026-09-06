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
        var medUpdate = Method(
            "EFT.HealthSystem.ActiveHealthController/MedEffect",
            "RegularUpdate"
        );
        check(
            medUpdate.Parameters.Single().ParameterType.FullName == "System.Single",
            "Consumable tick signature"
        );
        foreach (var field in new[] { "_foodDrink", "_interrupted" })
            check(
                types["EFT.HealthSystem.ActiveHealthController/MedEffect"]
                    .Fields.Any(f => f.Name == field && f.IsPublic),
                "Consumable hook field: " + field
            );
        check(
            medUpdate.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "HpPercent"
            ),
            "Food consumption occurs inside patched tick"
        );
        check(
            types["EFT.HealthSystem.ActiveHealthController/Effect"]
                .Methods.Single(m => m.Name == "Create" && m.HasGenericParameters)
                .Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.Name == "CreateInstance"
                ),
            "Each native med operation uses a fresh effect object"
        );
        Method("EFT.HealthSystem.ActiveHealthController/HealthBoost", "Started");
        foreach (var symptom in new[] { "Pain", "Tremor", "TunnelVision" })
            check(
                types["EFT.HealthSystem.ActiveHealthController/" + symptom]
                    .Methods.Any(m => m.IsConstructor && m.IsPublic),
                "Native allergy symptom constructor: " + symptom
            );
        check(
            types["EFT.HealthSystem.ActiveHealthController/MedEffect"]
                .Fields.Any(f => f.Name == "_medKit" && f.IsPublic),
            "Medicine resource binding"
        );
        Method("EFT.HealthSystem.ActiveHealthController/MedEffect", "Residue");
        var gridCompatibility = Method("EFT.InventoryLogic.Grid", "CheckCompatibility");
        check(
            gridCompatibility.ReturnType.FullName == "System.Boolean"
                && gridCompatibility.Parameters.Single().Name == "item",
            "Grid restriction postfix binding"
        );
        var movePath = Method("EFT.InventoryLogic.ItemManipulator", "MovePathCheck");
        check(
            movePath
                .Parameters.Select(p => p.Name)
                .SequenceEqual(new[] { "item", "to", "itemController" }),
            "Move/swap validation binding"
        );
        var addItem = types["EFT.InventoryLogic.ItemManipulator"]
            .Methods.Single(m => m.Name == "Add" && m.Parameters.Count == 5);
        check(
            addItem
                .Parameters.Select(p => p.Name)
                .SequenceEqual(
                    new[] { "item", "to", "itemController", "simulate", "ignoreRestrictions" }
                ),
            "Direct add restriction binding and restore bypass"
        );
        var transferStack = Method("EFT.InventoryLogic.ItemManipulator", "TransferMaxStackCount");
        check(
            transferStack
                .Parameters.Take(2)
                .Select(p => p.Name)
                .SequenceEqual(new[] { "source", "target" }),
            "Merge/transfer restriction binding"
        );
        check(
            types["EFT.InventoryLogic.MobContainer"].Properties.Any(p => p.Name == "isSecured"),
            "Native secure-container identity"
        );
        var regenTick = Method(
            "EFT.HealthSystem.ActiveHealthController/HealthBoost",
            "RegularUpdate"
        );
        check(
            regenTick.Parameters.Single().Name == "deltaTime",
            "Regeneration prefix delta-time binding"
        );
        var effectUpdate = Method("EFT.HealthSystem.ActiveHealthController/Effect", "ManualUpdate");
        check(
            effectUpdate.Body.Instructions.Any(i => i.OpCode == OpCodes.Sub)
                && effectUpdate.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Starg_S || i.OpCode == OpCodes.Starg
                ),
            "Native lifecycle clamps the final tick delta"
        );
        check(
            !types["EFT.HealthSystem.ActiveHealthController/HealthBoost"]
                .Interfaces.Any(i => i.InterfaceType.Name == "IRestorable"),
            "Regeneration carrier cannot persist as an unrelated health effect"
        );
        check(
            types["EFT.HealthSystem.ActiveHealthController/HealthBoost"]
                .Methods.Any(m => m.IsConstructor && m.IsPublic),
            "Native regeneration carrier constructor is available"
        );
        check(
            types["EFT.HealthSystem.ActiveHealthController/Effect"]
                .Methods.Single(m => m.Name == "NextState")
                .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Started"),
            "Carrier registration precedes immediate Started callback"
        );
        Method("EFT.HealthSystem.EffectsSettings/ProbabilitySetting", "Try");
        Method("EFT.BaseSkill", "SetCurrent");
        var experienceSetter = Method("EFT.Profile", "set_Experience");
        check(
            experienceSetter.Parameters.Single().Name == "value"
                && experienceSetter.Parameters[0].ParameterType.FullName == "System.Int32",
            "Profile award setter parameter binding"
        );
        bool CallsExperienceSetter(MethodDefinition method, string owner) =>
            method.Body.Instructions.Any(i =>
                i.Operand is MethodReference m
                && m.Name == "set_Experience"
                && m.DeclaringType.FullName == owner
            );
        check(
            CallsExperienceSetter(experienceSetter, "EFT.ProfileInfo"),
            "Award wrapper writes ProfileInfo"
        );
        check(
            CallsExperienceSetter(
                Method("EFT.InventoryLogic.Operations.ExamineOperation", "ExecuteInternal"),
                "EFT.InventoryLogic.IInventoryProfileInfo"
            ),
            "Examination awards through profile interface"
        );
        check(
            types["EFT.Profile"]
                .Interfaces.Any(i =>
                    i.InterfaceType.FullName == "EFT.InventoryLogic.IInventoryProfileInfo"
                ),
            "Profile implements examination interface"
        );
        check(
            CallsExperienceSetter(
                Method("EFT.InventoryLogic.ItemManipulator", "FinishConditional"),
                "EFT.Profile"
            ),
            "Client quest rewards use award wrapper"
        );
        check(
            CallsExperienceSetter(
                Method("EFT.ProfileUpdatesHandler", "ApplyExperience"),
                "EFT.ProfileInfo"
            ),
            "Backend XP reconciliation bypasses award wrapper"
        );
        var treatment = Method("EFT.OfflineStatisticManager", "ExperienceGained");
        check(
            treatment.Parameters.Single().Name == "experience"
                && treatment.Parameters[0].ParameterType.FullName == "System.Single",
            "Treatment XP prefix parameter binding"
        );
        check(
            CallsExperienceSetter(treatment, "EFT.ProfileInfo"),
            "Treatment XP bypasses profile award hook to prevent double scaling"
        );
        Method("EFT.MovementContext", "get_StateSprintSpeedLimit");
        Method("EFT.MovementContext", "RefreshObstacleRestrictions");
        Method("EFT.MovementContext", "ProcessSpeedLimits");
        foreach (var name in new[] { "OnTriggerEnter", "OnTriggerExit" })
        {
            var target = Method("EFT.Interactive.TreeInteractive", name);
            check(
                target.Parameters.Count == 1
                    && target.Parameters[0].Name == "col"
                    && target.Parameters[0].ParameterType.FullName == "UnityEngine.Collider",
                "Bush trigger collider binding: " + name
            );
        }
        var speedLimit = types["EFT.MovementContext"]
            .Methods.Single(m => m.Name == "AddStateSpeedLimit" && m.Parameters.Count == 2);
        check(
            speedLimit.Parameters[0].Name == "speedLimit"
                && speedLimit.Parameters[0].ParameterType.FullName == "System.Single"
                && speedLimit.Parameters[1].Name == "cause"
                && speedLimit.Parameters[1].ParameterType.FullName == "EFT.Player/ESpeedLimit",
            "Bush speed prefix arguments"
        );
        var firGetter = Method("EFT.Hideout.ItemRequirement", "get_IsSpawnedInSession");
        check(
            firGetter.ReturnType.FullName == "System.Boolean" && !firGetter.IsStatic,
            "Hideout FiR getter signature"
        );
        var suitable = types["EFT.Hideout.ItemRequirement"]
            .Methods.Single(m => m.Name == "IsSuitableItem" && m.IsStatic);
        check(
            suitable.Body.Instructions.Any(i =>
                i.Operand is MethodReference m && m.Name == firGetter.Name
            ),
            "Hideout suitability reads FiR through getter"
        );
        check(
            types["EFT.Hideout.ItemRequirementPanel"]
                .Methods.Any(m =>
                    m.HasBody
                    && m.Body.Instructions.Any(i =>
                        i.Operand is MethodReference called && called.FullName == firGetter.FullName
                    )
                ),
            "Hideout requirement panel reads same FiR getter"
        );
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
