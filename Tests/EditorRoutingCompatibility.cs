using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace WTT.Campaigns.Tests;

// Load only assemblies; do not create a host, start SPT, or open profile storage.
internal static class EditorRoutingCompatibility
{
    internal static void Run(string runtimeDirectory, string serverAssembly)
    {
        CheckAssembly(
            Path.GetDirectoryName(typeof(SPTarkov.Server.Core.Routers.HttpRouter).Assembly.Location)!,
            serverAssembly,
            "reference package"
        );
        CheckAssembly(Path.GetFullPath(runtimeDirectory), serverAssembly, "installed server");
    }

    private static void CheckAssembly(string nativeDirectory, string serverAssembly, string label)
    {
        var context = new NativeContext(nativeDirectory, Path.GetDirectoryName(Path.GetFullPath(serverAssembly))!);
        try
        {
            var mod = context.LoadFromAssemblyPath(Path.GetFullPath(serverAssembly));
            var core = context.LoadFromAssemblyPath(Path.Combine(nativeDirectory, "SPTarkov.Server.Core.dll"));
            var sessions = Activator.CreateInstance(mod.GetType("WTT.Campaigns.Server.Editor.EditorSessions", true)!, new object?[6]);
            var patchType = mod.GetType("WTT.Campaigns.Server.Editor.EditorRequestGate", true)!;
            var patch = Activator.CreateInstance(patchType, sessions)!;
            var target = (MethodInfo?)
                patchType.GetMethod("GetTargetMethod", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(patch, null);
            Require(
                target != null && target.DeclaringType == core.GetType("SPTarkov.Server.Core.Routers.HttpRouter"),
                "Gate resolves on " + label
            );
            Require(
                target!.Name == "HandleRouteAsync" && target.ReturnType == typeof(ValueTask<bool>),
                "Gate uses the shared dispatch contract on " + label
            );
            var wrapperType = target.GetParameters()[2].ParameterType;
            var wrapper = Activator.CreateInstance(
                wrapperType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object?[] { "unchanged" },
                null
            )!;
            var output = wrapperType.GetProperty("Output")!;
            var streamed = wrapperType.GetProperty("StreamedBody");
            var id = "0123456789abcdef01234567";
            var identity = Activator.CreateInstance(core.GetType("SPTarkov.Server.Core.Models.Common.MongoId", true)!, id)!;
            var scratch =
                (ConcurrentDictionary<string, byte>)
                    mod.GetType("WTT.Campaigns.Server.Editor.EditorSessionRegistry", true)!
                        .GetField("ScratchIds", BindingFlags.Static | BindingFlags.NonPublic)!
                        .GetValue(null)!;
            var prefix = patchType.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
            var request = new DefaultHttpContext().Request;
            object?[] args = { request, identity, wrapper, default(ValueTask<bool>) };
            request.Path = "/client/game/profile/items/moving";
            Require(
                (bool)prefix.Invoke(null, args)! && (string?)output.GetValue(wrapper) == "unchanged",
                "Normal gameplay passes through unchanged on " + label
            );
            scratch[id] = 0;
            Require(
                !(bool)prefix.Invoke(null, args)! && ((ValueTask<bool>)args[3]!).Result,
                "Scratch mutations are handled without dispatch on " + label
            );
            var error = JObject.Parse((string)output.GetValue(wrapper)!);
            Require(
                (int?)error["err"] == 228 && error["Error"] != null && (streamed == null || streamed.GetValue(wrapper) == null),
                "Blocked response remains native string JSON on " + label
            );
            output.SetValue(wrapper, "read-through");
            request.Path = "/client/locations";
            Require(
                (bool)prefix.Invoke(null, args)! && (string?)output.GetValue(wrapper) == "read-through",
                "Editor setup/read requests retain native output on " + label
            );
            request.Path = "/wtt-campaigns/editor/status";
            Require((bool)prefix.Invoke(null, args)!, "Editor session recovery remains reachable on " + label);
            scratch.TryRemove(id, out _);
            var savePatchType = mod.GetType("WTT.Campaigns.Server.Editor.EditorScratchSavePatch", true)!;
            var savePatch = Activator.CreateInstance(savePatchType)!;
            var saveTarget = (MethodInfo?)
                savePatchType.GetMethod("GetTargetMethod", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(savePatch, null);
            Require(
                saveTarget?.DeclaringType == core.GetType("SPTarkov.Server.Core.Servers.SaveServer")
                    && saveTarget.ReturnType == typeof(Task<long>),
                "Scratch save target also resolves on " + label
            );
            Console.WriteLine(
                "Editor routing: compiled patch target, blocked requests, native reads and scratch save target passed against "
                    + label
                    + "."
            );
        }
        finally
        {
            context.Unload();
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private sealed class NativeContext(string nativeDirectory, string modDirectory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name!.StartsWith("System.", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal))
                return null;
            var directory = name.Name.StartsWith("WTT-Campaigns.", StringComparison.Ordinal) ? modDirectory : nativeDirectory;
            var file = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
        }
    }
}
