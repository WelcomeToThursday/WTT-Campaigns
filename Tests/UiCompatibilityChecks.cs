using Mono.Cecil;

namespace SeasonalPerks.Tests;

internal static class UiCompatibilityChecks
{
    internal static void Run(string path, string? clientPath = null)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var types = assembly.MainModule.GetTypes().ToDictionary(type => type.FullName);
        var count = 0;
        void Check(bool value, string description)
        {
            count += value ? 1 : throw new InvalidOperationException(description);
        }
        foreach (var name in new[] { "EFT.UI.MenuScreen", "EFT.UI.SkillsAndMasteringScreen" })
        {
            var method = types[name]
                .Methods.Single(value =>
                    value.Name == "Show" && value.Parameters.FirstOrDefault()?.ParameterType.FullName == "EFT.Profile"
                );
            Check(
                method.Parameters[0].Name == "profile" && method.Parameters[0].ParameterType.FullName == "EFT.Profile",
                name + " Show profile binding"
            );
        }
        Check(
            types["EFT.UI.PreloaderUI"]
                .Fields.Any(field => field.Name == "_loader" && field.FieldType.FullName == "UnityEngine.GameObject" && field.IsPublic),
            "Native loading indicator is available to the seasonal creation overlay"
        );
        foreach (var field in new[] { "_masteringTab", "_skillsScreen", "_skillMasterTabGroup" })
        {
            Check(types["EFT.UI.SkillsAndMasteringScreen"].Fields.Any(value => value.Name == field), "Native skills field " + field);
        }
        var skillsScreen = types["EFT.UI.SkillsAndMasteringScreen"];
        Check(
            skillsScreen
                .Methods.Single(method => method.Name == "Awake")
                .Body.Instructions.Any(instruction =>
                    instruction.OpCode == Mono.Cecil.Cil.OpCodes.Stfld
                    && instruction.Operand is FieldReference field
                    && field.Name == "_skillMasterTabGroup"
                ),
            "Native Awake initializes the skills tab group"
        );
        var showInstructions = skillsScreen.Methods.Single(method => method.Name == "Show").Body.Instructions;
        var activation = showInstructions.First(instruction =>
            instruction.Operand is MethodReference method && method.Name == "ShowGameObject"
        );
        var groupAccess = showInstructions.First(instruction =>
            instruction.Operand is FieldReference field && field.Name == "_skillMasterTabGroup"
        );
        Check(activation.Offset < groupAccess.Offset, "Native Show activates the screen before accessing its tab group");
        if (clientPath != null)
        {
            using var client = AssemblyDefinition.ReadAssembly(clientPath);
            var patch = client.MainModule.GetType("SeasonalPerks.Client.Patches.UI.SkillsTabPatch");
            var initialization = patch.Methods.Single(method =>
                method.HasBody
                && method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference called
                    && called.DeclaringType.FullName == "SeasonalPerks.Client.SeasonalSkillsTab"
                    && called.Name == "Initialize"
                )
            );
            Check(
                initialization.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "PatchPostfixAttribute")
                    && !patch.Methods.Any(method =>
                        method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "PatchPrefixAttribute")
                    ),
                "Perks tab initializes after native Show, including first activation"
            );
            Check(
                initialization.Body.ExceptionHandlers.Any(handler => handler.CatchType?.FullName == "System.Exception"),
                "Optional perks initialization cannot throw through native Skills Show"
            );
        }
        Check(
            types["EFT.UI.MenuScreen"].Fields.Any(value => value.Name == "_playerButton" && value.FieldType.Name == "DefaultUIButton"),
            "Native menu button"
        );
        Check(
            types["EFT.UI.TabGroup"].Fields.Any(value => value.Name == "_tabs" && value.FieldType.FullName == "Tab[]"),
            "Extensible native tabs"
        );
        Check(
            types["EFT.UI.TabGroup"].Methods.Any(value => value.Name == "SelectionChangedHandler" && value.IsPublic),
            "Native tab selection handler"
        );
        Check(types["Tab"].Events.Any(value => value.Name == "OnSelectionChanged"), "Native tab selection event");
        foreach (var name in new[] { "Show", "TryHide" })
        {
            Check(types["EFT.UI.ITabController"].Methods.Any(value => value.Name == name), "Tab controller " + name);
        }
        var translate = types["EFT.InputSystem.InputNode"].Methods.Single(value => value.Name == "TranslateInput");
        Check(
            translate.Parameters.Select(value => value.Name).SequenceEqual(new[] { "commands", "axes", "shouldLockCursor" }),
            "Input patch argument names"
        );
        Check(
            translate.Parameters[1].ParameterType.IsByReference && translate.Parameters[2].ParameterType.IsByReference,
            "Input patch reference arguments"
        );
        Check(types["EFT.InputSystem.UIInputRoot"].BaseType.FullName == "EFT.InputSystem.InputNode", "UI root input interception");
        Check(types["EFT.InputSystem.ECursorResult"].Fields.Any(value => value.Name == "ShowCursor"), "Overlay cursor state");
        Check(
            types["EFT.UI.PlayerModelView"]
                .Methods.Any(value =>
                    value.Name == "Show" && value.Parameters[0].ParameterType.FullName == "EFT.PlayerVisualRepresentation"
                ),
            "Independent equipment preview overload"
        );
        Check(types["EFT.UI.PlayerModelView"].Fields.Any(value => value.Name == "_progressSpinner"), "Model loading indicator binding");
        Check(types["EFT.UI.CameraImage"].Methods.Any(value => value.Name == "InitCamera"), "Native render texture lifecycle");
        Check(
            types["EFT.InventoryEquipmentDescriptor"].Methods.Any(value => value.Name == "OnJSONDeserialized"),
            "Native equipment-tree deserialization"
        );
        Check(
            types["EFT.PlayerVisualRepresentationDescriptor"]
                .Fields.Any(value => value.Name == "Equipment" && value.FieldType.FullName == "EFT.InventoryEquipmentDescriptor"),
            "Appearance-only profile descriptor"
        );
        var reconnect = types["EFT.TarkovApplication"].Methods.Single(method => method.Name == "RecreateBackend");
        Check(
            reconnect.IsPublic
                && reconnect.Parameters.Count == 2
                && reconnect.Parameters[1].Name == "force"
                && reconnect.Parameters[1].ParameterType.FullName == "System.Boolean",
            "Native reconnect supports forced same-mode profile switching"
        );
        Console.WriteLine($"UI compatibility: {count} checks passed against {Path.GetFileName(path)}.");
    }
}
