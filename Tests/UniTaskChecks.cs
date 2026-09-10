using System.Reflection;
using Mono.Cecil;

namespace SeasonalPerks.Tests;

internal static class UniTaskChecks
{
    internal static void Run(AssemblyDefinition client, Assembly runtime, Action<bool, string> check)
    {
        var calls = client.MainModule.GetMemberReferences().OfType<MethodReference>().ToArray();
        check(
            !calls.Any(m => m.DeclaringType.FullName == "System.Threading.Tasks.Task" && m.Name is "Delay" or "Yield"),
            "Unity waits no longer use thread-pool timers or Task.Yield"
        );
        check(
            calls.Any(m => m.DeclaringType.FullName == "Cysharp.Threading.Tasks.UniTask" && m.Name == "NextFrame"),
            "Loader and subtitle waits cross Unity frames"
        );
        var skills = client.MainModule.Types.Single(t => t.Name == "SeasonalSkillsTab");
        check(
            skills.Methods.Single(m => m.Name == "TryHide").ReturnType.FullName == "System.Threading.Tasks.Task`1<System.Boolean>",
            "Native skills interface keeps its Task contract"
        );
        var cache = client.MainModule.Types.Single(t => t.Name == "ImageRequestCache");
        check(
            cache.Methods.Single(m => m.Name == "GetAsync").ReturnType.FullName == "System.Threading.Tasks.Task`1<System.Byte[]>",
            "Shared downloads remain safe for multiple awaiters"
        );

        // Exercise the actual client without creating textures, making HTTP requests, or running a Unity loop.
        var loader = runtime.GetType("SeasonalPerks.Client.UI.SeasonImageLoader")!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var operation = loader
            .GetMethod("LoadAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, ["unused-cancelled-request", cancellation.Token])!;
        var awaiter = operation.GetType().GetMethod("GetAwaiter")!.Invoke(operation, null)!;
        check((bool)awaiter.GetType().GetProperty("IsCompleted")!.GetValue(awaiter)!, "Cancelled image request finishes immediately");
        var cancelled = false;
        try
        {
            awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is OperationCanceledException error)
        {
            cancelled = error.CancellationToken == cancellation.Token;
        }
        check(cancelled, "Image loading preserves cancellation before touching HTTP or Unity objects");
        Console.WriteLine("UniTask timing/interface contracts and cancelled image request passed offline.");
    }
}
