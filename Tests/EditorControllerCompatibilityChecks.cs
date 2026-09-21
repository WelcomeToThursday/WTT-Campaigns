using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class EditorControllerCompatibilityChecks
{
    internal static void Run(AssemblyDefinition assembly, Action<bool, string> check)
    {
        var coordinator = assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.Editor.RaidEditor");
        var controllers = new[] { "Ai", "Catalog", "Map" }
            .Select(name => assembly.MainModule.GetType("WTT.Campaigns.Client.Authoring.Controllers.Editor" + name + "Controller"))
            .ToArray();
        foreach (var controller in controllers)
        {
            check(controller != null && !controller.IsPublic, "Controller is internal to its feature namespace: " + controller?.Name);
            check(
                !controller!.Fields.Any(f => f.FieldType.FullName == coordinator.FullName),
                "Controller has no concrete coordinator reference: " + controller.Name
            );
            check(
                controller.Methods.Any(m => m.Name == "Bind")
                    && controller.Methods.Any(m => m.Name == "Reset")
                    && controller.Methods.Any(m => m.Name == "Dispose"),
                "Controller has explicit binding and lifetime boundaries: " + controller.Name
            );
            check(
                !AllTypes(controller)
                    .SelectMany(t => t.Methods)
                    .Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions)
                    .Any(i => i.Operand is FieldReference f && f.DeclaringType.FullName == coordinator.FullName),
                "Controller cannot reach into coordinator state: " + controller.Name
            );
        }
        var buildView = coordinator.Methods.Single(m => m.Name == "BuildView");
        foreach (var controller in controllers)
            check(Calls(buildView, controller.FullName, "Bind"), "The real editor binds the extracted controller: " + controller.Name);
        var dispose = coordinator.Methods.Single(m => m.Name == "OnDestroy");
        foreach (var controller in controllers)
            check(Calls(dispose, controller.FullName, "Dispose"), "Editor destruction disposes the controller: " + controller.Name);

        var catalog = controllers[1];
        var cancel = catalog.Methods.Single(m => m.Name == "CancelPlacement");
        check(
            Calls(cancel, "WTT.Campaigns.Client.Authoring.Scenes.SceneOperationLifetime", "Cancel"),
            "Placement teardown cancels its pending load before release"
        );
        check(
            Calls(cancel, "WTT.Campaigns.Client.Authoring.Scenes.SceneLootModel", "Release")
                && cancel.Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Dispose", DeclaringType.Name: "Model" }),
            "Placement teardown preserves separate native-pool and bundle-model release paths"
        );
        var reset = catalog.Methods.Single(m => m.Name == "Reset");
        check(
            Calls(reset, "WTT.Campaigns.Client.Authoring.Scenes.SceneCatalogRequestState", "Reset")
                && Calls(reset, catalog.FullName, "CancelPlacement"),
            "Session reset invalidates catalog requests and cancels placement"
        );
        var aiEdit = controllers[0].Methods.Single(m => m.Name == "EditAi");
        var mapEdit = controllers[2].Methods.Single(m => m.Name == "MapEdit");
        foreach (var edit in new[] { aiEdit, mapEdit })
            check(
                Calls(edit, "WTT.Campaigns.Client.Authoring.RaidEditorSession", "Edit"),
                "Extracted edits retain the session undo/validation boundary: " + edit.Name
            );
    }

    private static bool Calls(MethodDefinition method, string type, string name) =>
        method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name);

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type) =>
        new[] { type }.Concat(type.NestedTypes.SelectMany(AllTypes));
}
