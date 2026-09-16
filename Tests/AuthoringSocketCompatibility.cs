using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class AuthoringSocketCompatibility
{
    internal static void Run(string game, string clientPath, string serverPath)
    {
        using var native = AssemblyDefinition.ReadAssembly(
            Path.Combine(game, "BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll")
        );
        using var client = AssemblyDefinition.ReadAssembly(clientPath);
        using var server = AssemblyDefinition.ReadAssembly(serverPath);
        using var spt = AssemblyDefinition.ReadAssembly(Path.Combine(game, "SPT_Runtime/SPTarkov.Server.Core.dll"));
        var receive = native.MainModule.GetType("EFT.Communications.LongPollingWebSocketRequest").Methods.Single(m => m.Name == "Receive");
        Require(
            receive.ReturnType.FullName == "System.Threading.Tasks.Task"
                && receive.Parameters.Count == 2
                && receive.Parameters[0].Name == "webSocket"
                && receive.Parameters[0].ParameterType.FullName == "System.Net.WebSockets.WebSocket",
            "Native receive hook exposes the connected socket and returns Task"
        );
        var getter = native
            .MainModule.GetType("EFT.Communications.LongPollingRequestAbstract")
            .Properties.Single(p => p.Name == "OnReceiveMessage")
            .GetMethod;
        Require(
            getter.ReturnType.FullName == "System.Action`2<System.Int64,System.Byte[]>",
            "Native callback signature matches the reply filter"
        );
        var nativeCalls = Calls(native.MainModule.GetType("EFT.Communications.LongPollingWebSocketRequest"));
        Require(nativeCalls.Any(c => c.Name == getter.Name), "Native receive loop dispatches through the patched callback getter");
        Require(!nativeCalls.Any(c => c.Name == "SendAsync"), "Native notification loop has no competing outbound application sender");
        var bridgeCalls = Calls(client.MainModule.GetType("WTT.Campaigns.Client.Authoring.AuthoringSocket"));
        Require(
            !bridgeCalls.Any(c => c.DeclaringType.FullName == "System.Net.WebSockets.ClientWebSocket"),
            "Editor constructs no additional ClientWebSocket"
        );
        Require(
            !bridgeCalls.Any(c =>
                c.DeclaringType.FullName.StartsWith("System.Net.WebSockets.")
                && c.Name is "ReceiveAsync" or "Abort" or "Dispose" or "CloseAsync" or "CloseOutputAsync"
            ),
            "Editor does not receive from or manage the native socket"
        );
        var registration = client
            .MainModule.GetType("WTT.Campaigns.Client.Patches.PatchRegistration")
            .Methods.Single(m => m.Name == "EnableSession");
        foreach (var name in new[] { "AuthoringNotificationSocket", "AuthoringNotificationReply" })
        {
            Require(
                registration.Body.Instructions.Any(i => i.Operand is MethodReference c && c.DeclaringType.Name == name),
                "Notification hook is registered: " + name
            );
        }

        var handler = server.MainModule.GetType("WTT.Campaigns.Server.Routing.AuthoringSocketHandler");
        Require(
            handler
                .Methods.Single(m => m.Name == "AcceptsMapRequest")
                .Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.Name == "AcceptsMapRequest" && m.DeclaringType.Name == "Session"
                ),
            "The live map socket uses the tested editor protocol and identity gate"
        );
        Require(
            handler.Interfaces.Single().InterfaceType.Name == "ISptWebSocketMessageHandler",
            "Editor extends the existing SPT message connection"
        );
        Require(
            handler.Methods.Single(m => m.IsConstructor).Parameters.All(p => p.ParameterType.FullName == "System.IServiceProvider"),
            "Message handler defers dependencies to avoid notification construction cycles"
        );
        Require(
            Calls(handler).Any(c => c.DeclaringType.Name == "SptWebSocketConnectionHandler" && c.Name == "SendMessageAsync"),
            "Editor uses SPT's synchronized notification sender"
        );
        var sender = spt.MainModule.GetType("SPTarkov.Server.Core.Servers.Ws.SptWebSocketConnectionHandler");
        var previewCalls = Calls(client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Preview.ItemPreviewClient"));
        Require(
            previewCalls.Any(c => c.DeclaringType.Name == "AuthoringSocket" && c.Name == "Send"),
            "Item previews use the existing authoring transport"
        );
        Require(
            previewCalls.Any(c => c.DeclaringType.Name == "ItemViewFactory" && c.Name == "LoadItemIcon")
                && !previewCalls.Any(c => c.Name == "GetItemSpriteAsync"),
            "Item previews use the trader's cached icon path rather than uncached rendering"
        );
        var traderIconCalls = native
            .MainModule.GetType("EFT.UI.DragAndDrop.ItemView")
            .Methods.Single(m => m.Name == "RefreshIcon")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>();
        Require(
            traderIconCalls.Any(c => c.DeclaringType.Name == "ItemViewFactory" && c.Name == "LoadItemIcon"),
            "Preview and installed trader views share the same native icon entry point"
        );
        var iconCalls = Calls(native.MainModule.GetType("ItemIconCreator"));
        Require(
            iconCalls.Any(c => c.Name == "TryGetCachedIcon") && iconCalls.Any(c => c.Name == "LoadFromUserCacheAsync"),
            "Native trader icon provider reuses both memory and disk caches"
        );
        foreach (var container in new[] { "Slot", "Grid" })
            Require(
                previewCalls.Any(c => c.DeclaringType.Name == container && c.Name == "Add"),
                "Preview verifies native container placement: " + container
            );
        Require(
            previewCalls.Any(c => c.DeclaringType.Name == "StackSlot" && c.Name == "FinalizeDeserialization"),
            "Ammunition stacks are finalized through native capacity checks"
        );
        Require(
            !previewCalls.Any(c => c.Name is "FlatItemsToTree" or "AddItemWithoutRestrictions"),
            "Preview does not accept unrestricted tree deserialization as verification"
        );
        var builder = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Preview.ItemPreviewClient");
        var lockedPart = builder.Methods.Single(m => m.Name == "RestoreLockedPart");
        var lockedCalls = lockedPart.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        foreach (var name in new[] { "get_Locked", "get_ContainedItem", "CheckCompatibility", "GetConflictingSlot", "CheckConflictingItems", "AddWithoutRestrictions" })
            Require(lockedCalls.Any(c => c.Name == name), "Built-in parts retain native validation: " + name);
        Require(lockedPart.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Throw),
            "Incompatible built-in parts are rejected");
        Require(!previewCalls.Any(c => c.Name == "set_Locked"), "Reconstruction leaves native slot locks intact");
        Require(builder.Methods.Where(m => m.HasBody && m != lockedPart)
            .All(m => !m.Body.Instructions.Any(i => i.Operand is MethodReference c && c.Name == "AddWithoutRestrictions")),
            "Only the validated built-in-part path can bypass a gameplay insertion lock");
        Require(builder.Methods.Single(m => m.Name == "Build").Body.Instructions
            .Any(i => i.Operand is MethodReference c && c.Name == "RestoreLockedPart"),
            "The preview item builder restores locked native parts");
        Require(
            previewCalls.Any(c => c.Name == "ReleaseTemporary") && previewCalls.Any(c => c.Name == "Destroy"),
            "Preview releases its own temporary graphics resources"
        );
        var restock = server.MainModule.GetType("WTT.Campaigns.Server.Patches.Trading.CampaignTraderRestockPatch");
        Require(
            Calls(restock).Any(c => c.Name == "RestockAuthoredOffers"),
            "Authored stock restoration is attached to the native restock hook"
        );
        Require(
            Calls(sender).Any(c => c.DeclaringType.FullName == "System.Threading.SemaphoreSlim" && c.Name == "WaitAsync"),
            "Installed SPT sender serializes socket writes"
        );
        Console.WriteLine(
            "Shared editor socket: native hooks, notification coexistence, borrowed lifetime and server sender verified offline."
        );
    }

    private static MethodReference[] Calls(TypeDefinition type)
    {
        return Types(type)
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
    }

    private static IEnumerable<TypeDefinition> Types(TypeDefinition type)
    {
        yield return type;
        foreach (var child in type.NestedTypes.SelectMany(Types))
        {
            yield return child;
        }
    }

    private static void Require(bool pass, string message)
    {
        if (!pass)
        {
            throw new InvalidOperationException(message);
        }
    }
}
