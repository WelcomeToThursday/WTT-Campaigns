using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class PreviewNativeChecks
{
    internal static void CheckEscape(ModuleDefinition native, ModuleDefinition client)
    {
        var runtimeTypes = client.GetTypes().Where(t => t.FullName.StartsWith("WTT.Campaigns.Client.Encounters.EncounterPreviewRuntime"));
        var profileCalls = runtimeTypes
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        if (
            !profileCalls
                .OfType<GenericInstanceMethod>()
                .Any(m => m.Name == "DeserializeObject" && m.GenericArguments.Any(a => a.FullName == "EFT.ProfileDescriptor[]"))
            || profileCalls
                .OfType<GenericInstanceMethod>()
                .Any(m => m.Name == "DeserializeObject" && m.GenericArguments.Any(a => a.FullName == "EFT.Profile[]"))
            || !profileCalls.Any(m =>
                m.DeclaringType.FullName == "EFT.Profile"
                && m.Name == ".ctor"
                && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "EFT.ProfileDescriptor"
            )
        )
            throw new InvalidOperationException(
                "Bot JSON must pass through native profile descriptors before constructing runtime profiles."
            );
        if (
            !native
                .GetType("EFT.Profile")
                .Methods.Any(m =>
                    m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "EFT.ProfileDescriptor"
                )
        )
            throw new InvalidOperationException("Native profile descriptor construction contract changed.");

        var escape = Convert.ToInt32(native.GetType("EFT.InputSystem.ECommand").Fields.Single(f => f.Name == "Escape").Constant);
        var filter = client.GetType("WTT.Campaigns.Client.Authoring.EditorRestrictions").Methods.Single(m => m.Name == "Filter");
        var instructions = filter.Body.Instructions;
        var remove = instructions.FirstOrDefault(i =>
            i.Operand is MethodReference m
            && m.Name == "Remove"
            && m.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<EFT.InputSystem.ECommand>")
        );
        var value = remove?.Previous;
        var constant = value?.OpCode.Code switch
        {
            Mono.Cecil.Cil.Code.Ldc_I4 => (int)value.Operand,
            Mono.Cecil.Cil.Code.Ldc_I4_S => Convert.ToInt32(value.Operand),
            _ => -1,
        };
        var branch = instructions.First(i => i.Operand is MethodReference m && m.Name == "get_AiPlaytestActive");
        if (
            remove == null
            || constant != escape
            || remove.Offset >= branch.Offset
            || instructions.TakeWhile(i => i != remove).Any(i => i.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Cond_Branch)
        )
            throw new InvalidOperationException("Preview Escape must be removed before either editor/playtest native input branch.");

        var restrictions = filter.DeclaringType;
        var inventoryGate = restrictions.Methods.Single(m => m.Name == "InventoryAllowed");
        var gateCalls = inventoryGate.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        if (
            !gateCalls.Any(m => m.Name == "get_AiPlaytestActive")
            || gateCalls.Any(m => m.Name == "get_MissionTestActive" || m.Name == "get_MissionGameplayActive")
            || instructions.Select(i => i.Operand).OfType<MethodReference>().Any(m => m.Name == "get_MissionTestActive")
            || instructions.Count(i => i.Operand is MethodReference m && m.Name == "RemoveAll") != 1
        )
            throw new InvalidOperationException("Every playable AI preview must allow native inventory and interaction commands.");

        var editor = client.GetType("WTT.Campaigns.Client.Authoring.RaidEditor");
        var reset = editor.Methods.Single(m => m.Name == "EndAiPreview" && m.Parameters.Count == 1);
        var closeInventory = reset.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "ToggleScreen");
        var cleanup = reset.Body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "EndEditorMissionRoute");
        if (closeInventory.Offset >= cleanup.Offset)
            throw new InvalidOperationException("Preview reset must close native inventory before disposing its loot.");
        var nativeClose = native.GetType("EFT.EftGamePlayerOwner").Methods.Single(m => m.Name == "CloseInventoryIfOpen");
        var closeCall = (MethodReference)closeInventory.Operand;
        if (
            !nativeClose.Body.Instructions.Any(i =>
                i.Operand is MethodReference m
                && m.Name == closeCall.Name
                && m.DeclaringType.FullName == closeCall.DeclaringType.FullName
                && m.Parameters.Count == closeCall.Parameters.Count
            )
        )
            throw new InvalidOperationException("Native inventory screen close contract changed.");

        var update = client.GetType("WTT.Campaigns.Client.Authoring.RaidEditor").Methods.Single(m => m.Name == "UpdateAiPreview");
        var failure = update.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "get_Failure");
        var handler = update.Body.ExceptionHandlers.Single(h => h.CatchType?.FullName == "System.Exception");
        if (
            failure.Offset < handler.TryStart.Offset
            || failure.Offset >= handler.TryEnd.Offset
            || !update.Body.Instructions.Any(i =>
                i.Offset >= handler.HandlerStart.Offset
                && i.Offset < handler.HandlerEnd.Offset
                && i.Operand is MethodReference m
                && m.Name == "EndAiPreview"
            )
        )
            throw new InvalidOperationException("Asynchronous wave failures must reach the editor reset handler.");
    }

    internal static void Run(ModuleDefinition module)
    {
        var types = module.GetTypes().ToDictionary(t => t.FullName);
        void Require(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Preview compatibility: " + description);
        }
        var health = types["EFT.HealthSystem.ActiveHealthController"];
        Require(health.Methods.Count(m => m.Name == "Kill") == 1, "all local lethal interception must pass the single native Kill method");
        var kill = health.Methods.Single(m => m.Name == "Kill");
        Require(
            kill.ReturnType.FullName == "System.Void"
                && kill.Parameters.Count == 1
                && kill.Parameters[0].ParameterType.Name == "EDamageType",
            "native Kill signature changed"
        );
        Require(
            kill.HasBody && kill.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_IsAlive"),
            "Kill must own the terminal alive-state transition"
        );
        foreach (var name in new[] { "ChangeHealth", "ApplyDamage", "FullRestoreBodyPart", "ChangeEnergy", "ChangeHydration" })
            Require(health.Methods.Any(m => m.Name == name && m.IsPublic), "missing health method " + name);
        var player = types["EFT.Player"];
        var creator = types["BotCreatorClient"];
        Require(
            creator.Fields.Any(f => f.Name == "_botRenders" && f.IsPublic),
            "preview cleanup must remove originating creator renderer-cache entries"
        );
        Require(
            creator.Methods.Any(m =>
                m.Name == "StoreBotRenderers"
                && m.ReturnType.FullName == "System.Void"
                && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "EFT.Player"
            ),
            "late renderer-cache writes must cross the preview admission guard"
        );
        var world = types["EFT.GameWorld"];
        Require(
            world.Methods.Any(m => m.Name == "DestroyLoot" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "IKillable"),
            "native corpse and dropped-item cleanup is required"
        );
        Require(
            world.Methods.Any(m =>
                m.Name == "UnregisterGrenade" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "Throwable"
            ),
            "native throwable cleanup is required"
        );
        foreach (var name in new[] { "SetEmptyHands", "SetSlotItem", "RecalculateEquipmentParams" })
            Require(player.Methods.Any(m => m.Name == name && m.IsPublic), "missing player equipment method " + name);
        Require(
            player.Methods.Any(m =>
                m.Name == "SetInventoryOpened"
                && m.Parameters.Count == 1
                && m.Parameters[0].Name == "opened"
                && m.Parameters[0].ParameterType.FullName == "System.Boolean"
            ),
            "inventory-open interception signature changed"
        );
        var inventoryShows = types["EFT.UI.InventoryScreen"].Methods.Where(m => m.Name == "Show").ToArray();
        Require(
            inventoryShows.Length > 0 && inventoryShows.All(m => m.ReturnType.FullName == "System.Void"),
            "inventory screen fallback must reject every Show overload before drag/drop can run"
        );
        Require(
            types["EFT.EftGamePlayerOwner"]
                .Methods.Any(m => m.Name == "ShowInventoryScreenLoot" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 3),
            "native world-container screen entry must be gated before opening"
        );
        var slot = types["EFT.InventoryLogic.Slot"];
        foreach (var name in new[] { "RemoveItemWithoutRestrictions", "AddWithoutRestrictions" })
            Require(slot.Methods.Any(m => m.Name == name && m.IsPublic), "missing slot method " + name);
        var commands = types["EFT.InputSystem.ECommand"];
        foreach (
            var name in new[]
            {
                "ToggleInventory",
                "BeginInteracting",
                "EndInteracting",
                "BeginSpecialInteracting",
                "EndSpecialInteracting",
            }
        )
            Require(commands.Fields.Any(f => f.Name == name), "missing interaction gate " + name);
    }
}
