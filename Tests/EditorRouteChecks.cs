using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WTT.Campaigns.Tests;

internal static class EditorRouteChecks
{
    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.RaidEditor");
        var capture = editor.Methods.Single(m => m.Name == "CapturePoint");
        Require(
            Reads(capture, "_flyPosition") && Reads(capture, "_flyRotation") && !Reads(capture, "_player"),
            "Route placement must follow the free camera even when the player remains at raid spawn."
        );
        Require(
            Calls(capture, "TryRouteFloor") && !Calls(capture, "PlayerScene"),
            "Route markers must bind to the floor at the camera, including its loaded scene."
        );
        Require(
            Calls(editor.Methods.Single(m => m.Name == "CaptureVolume"), "CapturePoint"),
            "Checkpoints, exits and barriers share camera placement."
        );
        var constructor = editor.Methods.Single(m => m.Name == ".ctor").Body.Instructions;
        Require(
            constructor.Any(i =>
                i.OpCode.Code == Code.Stfld
                && i.Operand is FieldReference f
                && f.Name == "_walkFromStart"
                && i.Previous.OpCode.Code == Code.Ldc_I4_1
            ),
            "A new editor must start walkthroughs at the start marker by default."
        );
        var walk = editor.Methods.Single(m => m.Name == "BeginWalkthrough").Body.Instructions;
        int CallIndex(string name) => walk.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == name);
        Require(
            CallIndex("Apply") < CallIndex("SyncTransforms") && CallIndex("SyncTransforms") < CallIndex("ClearPosition"),
            "Spawn clearance must see the applied layout's current colliders."
        );
        Require(
            CallIndex("Close") >= 0 && CallIndex("Teleport") > CallIndex("Close"),
            "Native camera restoration must finish before walkthrough teleport."
        );
        Require(
            Reads(editor.Methods.Single(m => m.Name == "BeginWalkthrough"), "_flyPosition")
                && Reads(editor.Methods.Single(m => m.Name == "BeginWalkthrough"), "_flyRotation")
                && Calls(editor.Methods.Single(m => m.Name == "Open"), "RestoreWalkCamera"),
            "Walkthrough must bookmark and restore the authoring camera separately from the player."
        );
        Require(
            Reads(editor.Methods.Single(m => m.Name == "RestoreWalkCamera"), "_walkCameraRaid"),
            "Camera restoration must not carry a previous raid's coordinates into another map."
        );
        var update = editor.Methods.Single(m => m.Name == "Update");
        Require(EndsFrameAfterWalkthroughExit(update), "Walkthrough exit must end this Update before reopening or handling Escape again.");
        var walkthroughUpdate = editor.Methods.Single(m => m.Name == "UpdateWalkthrough");
        var end = walkthroughUpdate.Body.Instructions.Single(i => i.Operand is MethodReference { Name: "EndWalkthrough" });
        Require(
            end.Next.OpCode.Code == Code.Ldc_I4_1 && end.Next.Next.OpCode.Code == Code.Ret,
            "Ending a walkthrough must report that the frame's input was consumed."
        );
        // Reproduce the old fallthrough: a consumed Escape continues into Open.
        // The guard must reject that path, not merely check that a bookmark exists.
        var fallthrough = new MethodDefinition("OldWalkthroughFallthrough", MethodAttributes.Static, client.MainModule.TypeSystem.Void);
        fallthrough.Body.Instructions.Add(Instruction.Create(OpCodes.Call, walkthroughUpdate));
        fallthrough.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        fallthrough.Body.Instructions.Add(Instruction.Create(OpCodes.Call, editor.Methods.Single(m => m.Name == "Open")));
        fallthrough.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Require(!EndsFrameAfterWalkthroughExit(fallthrough), "Regression guard rejects the old double-Escape camera reset.");
        var poll = editor
            .NestedTypes.Single(t => t.Name.StartsWith("<Poll>", StringComparison.Ordinal))
            .Methods.Single(m => m.Name == "MoveNext");
        Require(Calls(poll, "RefreshLoadedScenes"), "Each authoring poll must refresh live scenes before submitting draft edits.");
        var scenes = editor.Methods.Single(m => m.Name == "RefreshLoadedScenes");
        Require(
            Calls(scenes, "GetSceneAt") && Calls(scenes, "get_gameObject") && Calls(scenes, "get_isLoaded"),
            "Scene reporting must include live persistent objects as well as ordinary loaded scenes."
        );

        var common = native.MainModule.GetType("EFT.UI.CommonUI");
        Require(
            common.Fields.Any(f => f.Name == "EftBattleUIScreen" && f.IsPublic && f.FieldType.FullName == "EFT.UI.EftBattleUIScreen"),
            "Editor HUD suppression requires the installed native battle screen."
        );
        var battle = native.MainModule.Types.Single(t => t.Name == "BattleUIScreen`2");
        Require(
            battle.Fields.Any(f => f.FieldType.FullName == "EFT.UI.BattleStancePanel")
                && battle.Fields.Any(f => f.FieldType.FullName == "EFT.UI.InventoryScreenQuickAccessPanel"),
            "Stance, stamina and quick access remain children of the suppressed battle screen."
        );
        var mode = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorMode");
        Require(
            Reads(mode.Methods.Single(m => m.Name == "UpdateHud"), "EftBattleUIScreen"),
            "HUD suppression must include CommonUI's battle screen, not only GameUI."
        );
        var hud = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorHud");
        Require(
            Calls(hud.Methods.Single(m => m.Name == "Suppress"), "GetComponentsInChildren"),
            "HUD suppression must also cover independently animated child canvas groups."
        );
        Require(
            !Calls(hud.Methods.Single(m => m.Name == "SuppressObject"), "SetActive"),
            "HUD suppression must preserve native canvas lifecycle."
        );
        Require(
            Calls(mode.Methods.Single(m => m.Name == "Awake"), "add_willRenderCanvases")
                && Calls(mode.Methods.Single(m => m.Name == "OnDestroy"), "remove_willRenderCanvases"),
            "HUD animation suppression must use a paired pre-render subscription."
        );
        Require(
            !Calls(mode.Methods.Single(m => m.Name == "Update"), "Suppress")
                && mode.Methods.Single(m => m.Name == "UpdateHud").Body.ExceptionHandlers.Any(),
            "A presentation failure must not prevent editor session maintenance."
        );
        Console.WriteLine(
            "Editor routes/HUD: camera placement, start default, teleport ordering and native battle panel contracts passed offline."
        );
    }

    private static bool Reads(MethodDefinition method, string field) =>
        method.Body.Instructions.Any(i => i.OpCode.Code is Code.Ldfld or Code.Ldflda && i.Operand is FieldReference f && f.Name == field);

    private static bool Calls(MethodDefinition method, string name) =>
        method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == name);

    private static bool EndsFrameAfterWalkthroughExit(MethodDefinition update)
    {
        var call = update.Body.Instructions.Single(i => i.Operand is MethodReference { Name: "UpdateWalkthrough" });
        if (((MethodReference)call.Operand).ReturnType.FullName != "System.Boolean")
            return false;
        var next = call.Next;
        while (next?.OpCode.Code == Code.Nop)
            next = next.Next;
        // Follow the actual compiled path with UpdateWalkthrough returning true.
        next = next?.OpCode.Code switch
        {
            Code.Brfalse or Code.Brfalse_S => next.Next,
            Code.Brtrue or Code.Brtrue_S => (Instruction)next.Operand,
            _ => null,
        };
        var visited = new HashSet<Instruction>();
        while (next != null && visited.Add(next))
        {
            switch (next.OpCode.Code)
            {
                case Code.Ret:
                    return true;
                case Code.Nop:
                    next = next.Next;
                    break;
                case Code.Br:
                case Code.Br_S:
                case Code.Leave:
                case Code.Leave_S:
                    next = (Instruction)next.Operand;
                    break;
                default:
                    return false; // Any other work can process the same input twice.
            }
        }
        return false;
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
