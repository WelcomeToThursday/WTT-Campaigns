using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WTT.Campaigns.Tests;

internal static class EditorHomeCompatibilityChecks
{
    internal static void Run(AssemblyDefinition native, AssemblyDefinition client)
    {
        var types = native.MainModule.GetTypes().ToDictionary(t => t.FullName);
        var screen = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorHomeScreen");
        var controller = screen.NestedTypes.Single(t => t.Name == "Controller");
        var checks = 0;
        void Check(bool valid, string message)
        {
            if (!valid)
                throw new InvalidOperationException("Editor home: " + message);
            checks++;
        }
        Check(screen.BaseType.Name == "EftScreen`2", "Home participates in the native screen lifecycle.");
        Check(controller.BaseType.Name == "EftScreenController`2", "Home uses native environment and navigation state.");
        Check(
            EveryReturnCreatesPointerCanvas(screen.Methods.Single(m => m.Name == "Create")),
            "Every canvas-parent branch must create the editor's own canvas and raycaster before returning its screen."
        );
        var screenId = (int)screen.Fields.Single(f => f.Name == "ScreenType").Constant;
        Check(
            !types["EFT.UI.Screens.EEftScreenType"].Fields.Any(f => f.HasConstant && Equals(f.Constant, screenId)),
            "The editor registration must not replace any built-in EFT screen."
        );
        var manager = types["EFT.UI.Screens.ScreenManager`1"];
        foreach (var method in new[] { "RegisterScreen", "ReleaseScreen", "TryGetScreen", "RegisterInput", "UnregisterInput" })
            Check(manager.Methods.Any(m => m.Name == method && m.IsPublic), "Native screen manager API: " + method);
        var nativeController = manager.NestedTypes.Single(t => t.Name == "ScreenController`2");
        Check(
            nativeController.Methods.Any(m => m.Name == "ShowScreenAsync" && m.IsPublic && m.Parameters.Count == 2),
            "Native root transitions expose the awaited ShowScreenAsync overload."
        );
        var environment = types["EFT.UI.Screens.EftScreenManager/EftScreenController`2"];
        foreach (var property in controller.Properties)
        {
            if (property.Name == "ScreenType" || property.Name == "KeyScreen")
                continue;
            Check(
                environment.Properties.Any(p => p.Name == property.Name && p.GetMethod.IsVirtual),
                "Native environment switch: " + property.Name
            );
        }
        var button = types["EFT.UI.DefaultUIButton"];
        foreach (var field in new[] { "_headerLabel", "_minWidth", "OnClick", "OnMouseOver", "OnMouseOut" })
            Check(button.Fields.Any(f => f.Name == field && f.IsPublic), "Native button cloning API: " + field);
        Check(
            button.Fields.Single(f => f.Name == "OnClick").IsInitOnly,
            "Cloning a menu button must not inherit its character-navigation click listener."
        );
        Check(
            types["EFT.UI.MenuTaskBar"]
                .Methods.Single(m => m.Name == "OnScreenChanged")
                .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "TryGetValue"),
            "Native taskbar tolerates a screen without a built-in menu mapping."
        );
        var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorMode");
        Check(
            !editor.Fields.Any(f => f.FieldType.Name == "RaidEditorView"),
            "Startup home no longer depends on the in-raid prefab or its overlay sorting order."
        );
        var menu = client.MainModule.GetType("WTT.Campaigns.Client.Patches.UI.MenuEntry");
        Check(
            menu.Methods.Single(m => m.Name == "Postfix")
                .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "MenuReady"),
            "Authenticated menu startup dispatches the editor screen handoff."
        );
        Console.WriteLine($"Editor home: {checks} native screen, navigation and button contracts passed offline.");
    }

    // Follow both sides of the inherited-canvas branch. Merely finding AddComponent
    // in the method missed the original path that rendered without its own raycaster.
    private static bool EveryReturnCreatesPointerCanvas(MethodDefinition method)
    {
        var pending = new Queue<(Instruction Instruction, int Components)>();
        var visited = new HashSet<(Instruction, int)>();
        pending.Enqueue((method.Body.Instructions[0], 0));
        var returned = false;
        while (pending.TryDequeue(out var state))
        {
            if (!visited.Add(state))
                continue;
            var (instruction, components) = state;
            if (instruction.Operand is GenericInstanceMethod { Name: "AddComponent" } add)
            {
                components |= add.GenericArguments[0].FullName switch
                {
                    "UnityEngine.Canvas" => 1,
                    "UnityEngine.UI.GraphicRaycaster" => 2,
                    _ => 0,
                };
            }
            if (instruction.OpCode.FlowControl == FlowControl.Return)
            {
                if (components != 3)
                    return false;
                returned = true;
                continue;
            }
            if (instruction.OpCode.FlowControl == FlowControl.Throw)
                continue;
            if (instruction.Operand is Instruction target)
                pending.Enqueue((target, components));
            if (instruction.Operand is Instruction[] targets)
                foreach (var branch in targets)
                    pending.Enqueue((branch, components));
            if (instruction.OpCode.FlowControl != FlowControl.Branch && instruction.Next != null)
                pending.Enqueue((instruction.Next, components));
        }
        return returned;
    }
}
