using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WTT.Campaigns.Tests;

internal static class AiControlsChecks
{
    private static readonly string[] ExpectedActionGroups =
    {
        "AiTriggerGroup",
        "AiWaveWaitPreviousGroup",
        "AiRosterRoleGroup",
        "AiRosterDifficultyGroup",
        "AiRosterSpawnNextGroup",
        "AiRosterPatrolNextGroup",
        "AiPaceGroup",
        "AiCompletionGroup",
    };

    internal static void Run(AssemblyDefinition assembly, Action<bool, string> check)
    {
        var view = assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.Views.RaidEditorAiView");
        var editor = assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.Editor.RaidEditor");
        var controller = assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.Controllers.EditorAiController");

        static bool Calls(MethodDefinition method, string name) =>
            method.Body.Instructions.Any(i => i.Operand is MethodReference called && called.Name == name);
        var selection = Method(editor, "get_Selected");
        check(
            Calls(selection, "AiSelected") && Calls(selection, "get_Point") && !Calls(selection, "AiSelectedPoint"),
            "AI gizmos resolve the live draft point rather than discarding each frame's edits in a copied selection"
        );
        check(
            Calls(Method(controller, "AiSelectedPoint"), "Copy") && Calls(Method(editor, "EditPoint"), "AiSelectedPoint"),
            "Discrete AI edits retain their detached copy so session history captures the original point"
        );
        var finish = Method(editor, "FinishDrag").Body.Instructions.ToList();
        check(
            finish.FindIndex(i => i.Operand is MethodReference { Name: "RestorePoint" })
                < finish.FindIndex(i => i.Operand is MethodReference { Name: "EditPoint" }),
            "Finishing a gizmo drag restores the before-pose before committing one undoable edit"
        );
        check(
            Calls(Method(editor, "CancelDrag"), "RestorePoint") && !Calls(Method(editor, "CancelDrag"), "EditPoint"),
            "Cancelling a gizmo drag restores the draft without adding history"
        );

        var refresh = Method(controller, "RefreshAiWorkspace");
        var bind = Method(controller, "Bind");

        var nodes = EditorToolkitChecks.Nodes().Where(n => n.Id.StartsWith("Ai")).ToArray();
        check(
            nodes.Any(n => n.Id == "AiPlaytestGear" && n.Kind == "choice") && CallArguments(bind, "Dropdown").Contains("AiPlaytestGear"),
            "Playtest loadout choice is declared and bound in the AI tool"
        );
        var toolbar = WTT
            .Campaigns.Client.Authoring.Views.EditorLayoutSpec.Sections.Single(n => n.Id == "TransformToolbar")
            .Children.Single(n => n.Id == "EditorMapToolbar");
        foreach (var id in new[] { "AiObserve", "AiPlaytest", "AiPlaytestGear" })
            check(toolbar.Children.Any(n => n.Id == id), "Preview control belongs to the shared main toolbar: " + id);
        check(
            !StringOperands(view.Methods.Single(m => m.IsConstructor && m.IsStatic)).Any(s => s is "AiObserve" or "AiPlaytest"),
            "Switching away from AI does not hide global preview actions"
        );
        var generated = (
            Groups: nodes.Where(n => n.Id.EndsWith("Group")).Select(n => n.Id).ToHashSet(),
            Buttons: nodes.Where(n => n.Kind is "button" or "toggle").Select(n => n.Id).ToHashSet(),
            Fields: nodes.Where(n => n.Kind == "input").Select(n => n.Id).ToHashSet()
        );
        var generatedGroups = generated.Groups;
        var generatedButtons = generated.Buttons;
        var generatedFields = generated.Fields;

        check(
            ExpectedActionGroups.All(generatedGroups.Contains),
            "AI action controls are wrapped in the inspector groups consumed by refresh"
        );

        var consumedGroups = StringOperands(refresh)
            .Where(name => name.StartsWith("Ai", StringComparison.Ordinal) && name.EndsWith("Group", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        check(consumedGroups.SetEquals(generatedGroups), "Every AI inspector group consumed by refresh is declared in Toolkit");

        var boundButtons = CallArguments(bind, "Button")
            .Where(name => name.StartsWith("Ai", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        check(generatedButtons.SetEquals(boundButtons), "Every AI button declared in Toolkit is bound by BindAiControls");

        var generatedChoices = nodes.Where(n => n.Kind == "choice").Select(n => n.Id).ToHashSet();
        var boundChoices = CallArguments(bind, "Dropdown").Where(name => name.StartsWith("Ai", StringComparison.Ordinal)).ToHashSet();
        check(generatedChoices.SetEquals(boundChoices), "Every AI choice uses explicit selection and has a bound handler");

        var boundFields = CallArguments(bind, "Input")
            .Where(name => name.StartsWith("Ai", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        check(generatedFields.SetEquals(boundFields), "Every AI input declared in Toolkit is bound by BindAiControls");

        foreach (var fieldName in new[] { "CreationControls", "InspectorGroups", "InspectorFields" })
        {
            check(
                refresh.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == fieldName),
                "Leaving AI refreshes and hides the complete " + fieldName + " inventory"
            );
        }

        var workspace = Method(editor, "RefreshWorkspace");
        var presentIndex = workspace
            .Body.Instructions.Select((instruction, index) => (instruction, index))
            .First(pair => pair.instruction.Operand is MethodReference method && method.Name == "Present")
            .index;
        var windowsIndex = workspace
            .Body.Instructions.Select((instruction, index) => (instruction, index))
            .Take(presentIndex)
            .Last(pair => pair.instruction.Operand is FieldReference field && field.Name == "Windows")
            .index;
        var modeLoad = workspace.Body.Instructions.Skip(windowsIndex + 1).Take(3).ToArray();
        check(
            modeLoad.Length == 3
                && modeLoad[0].OpCode == OpCodes.Ldarg_0
                && modeLoad[1].Operand is FieldReference mode
                && mode.Name == "_mode"
                && modeLoad[2].OpCode != OpCodes.Ldstr,
            "AI workspace presents the native AI category instead of routing it through Captures"
        );
    }

    private static MethodDefinition Method(TypeDefinition? type, string name)
    {
        if (type == null)
            throw new InvalidOperationException("The compiled client is missing the AI control type.");
        return type.Methods.Single(method => method.Name == name);
    }

    private static IEnumerable<string> StringOperands(MethodDefinition method) =>
        method.Body.Instructions.Where(instruction => instruction.Operand is string).Select(instruction => (string)instruction.Operand);

    private static HashSet<string> CallArguments(MethodDefinition method, string callName)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var instructions = method.Body.Instructions;
        for (var index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].Operand is not MethodReference call || call.Name != callName)
                continue;
            var literal = PreviousString(instructions, index);
            if (literal != null)
                result.Add(literal);
        }
        return result;
    }

    private static string? PreviousString(Mono.Collections.Generic.Collection<Instruction> instructions, int index)
    {
        for (var i = index - 1; i >= 0 && index - i <= 8; i--)
            if (instructions[i].Operand is string value)
                return value;
        return null;
    }
}
