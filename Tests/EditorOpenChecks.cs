using Mono.Cecil;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorOpenChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new EditorOpenState();
        check(state.TryBegin(false), "A new editor session opens automatically");
        state.Fail();
        var attempts = 0;
        for (var frame = 0; frame < 10000; frame++)
            if (state.TryBegin(false)) attempts++;
        check(attempts == 0, "A failed editor does not retry and spam exceptions every frame");
        check(state.TryBegin(true), "The configured shortcut deliberately retries a failed editor");
        state.Fail();
        check(!state.TryBegin(false), "A failed deliberate retry blocks subsequent automatic attempts");
        state.Reset();
        check(state.TryBegin(false), "A new map can attempt editor initialization again");
    }

    internal static void Client(AssemblyDefinition assembly, Action<bool, string> check)
    {
        var view = assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.RaidEditorView");
        var constructor = view.Methods.Single(m => m.IsConstructor && !m.IsStatic);
        var calls = constructor.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToArray();
        check(calls.Any(m => m.DeclaringType.Name == "RaidEditorLayout" && m.Name == "Prepare"), "Client and SDK validation use the same prefab upgrade sequence");
        var cleanup = constructor.Body.ExceptionHandlers.Where(h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch)
            .SelectMany(h => constructor.Body.Instructions.Where(i => i.Offset >= h.HandlerStart.Offset && i.Offset < h.HandlerEnd.Offset))
            .Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToArray();
        check(cleanup.Any(m => m.DeclaringType.Name == "AssetBundle" && m.Name == "Unload"), "Failed construction releases its newly loaded bundle");
        check(cleanup.Any(m => m.Name == "Destroy") && cleanup.Any(m => m.Name == "SetActive"), "Failed construction deactivates and destroys its partial window hierarchy");
        var editor = assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.RaidEditor");
        check(editor.Methods.Single(m => m.Name == "Open").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "TryBegin"),
            "Every automatic editor opening respects the failure latch");
    }
}
