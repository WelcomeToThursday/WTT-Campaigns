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
