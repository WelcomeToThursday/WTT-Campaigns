using Mono.Cecil;
using UnityEngine.Scripting;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorMemoryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var initial in Enum.GetValues<GarbageCollector.Mode>())
        {
            var policy = new EditorCollectionPolicy();
            check(
                policy.Transition(false, initial) == null && policy.Filter(initial) == initial,
                "Normal gameplay retains its GC mode: " + initial
            );
            check(policy.Transition(true, initial) == GarbageCollector.Mode.Enabled, "Editor maps enable collection from " + initial);
            for (var i = 0; i < 1000; i++)
                if (policy.Transition(true, GarbageCollector.Mode.Enabled) != null)
                    throw new Exception("Repeated GC mode write");
            check(
                policy.Transition(false, GarbageCollector.Mode.Enabled) == initial,
                "Closing an editor map restores its prior GC mode: " + initial
            );
            check(policy.Transition(false, initial) == null, "Repeated GC cleanup is harmless.");
            policy.Transition(true, initial);
            check(
                policy.Filter(GarbageCollector.Mode.Disabled) == GarbageCollector.Mode.Enabled,
                "Native raid requests cannot disable collection during editor maps."
            );
            policy.Filter(GarbageCollector.Mode.Enabled); // Native menu setter can early-out before touching Unity.
            check(
                policy.Transition(false, GarbageCollector.Mode.Enabled) == GarbageCollector.Mode.Enabled,
                "Menu's enabled request supersedes the saved raid mode even if Unity's setter was skipped."
            );
            policy.Transition(true, initial);
            policy.Filter(GarbageCollector.Mode.Manual);
            check(
                policy.Transition(false, GarbageCollector.Mode.Enabled) == GarbageCollector.Mode.Manual,
                "A later native mode request is restored after the editor lease ends."
            );
        }
    }

    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        var wrapper = native.MainModule.GetType("InGameMemoryManagement").Properties.Single(p => p.Name == "GCEnabled").SetMethod;
        if (
            !wrapper.IsPublic
            || !wrapper.IsStatic
            || wrapper.Parameters.Count != 1
            || wrapper.Parameters[0].ParameterType.FullName != "System.Boolean"
        )
            throw new Exception("Native memory-mode wrapper changed.");
        var memory = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorMemory");
        var calls = memory
            .Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        if (calls.Any(m => m.Name is "Collect" or "CollectIncremental" or "EmptyWorkingSet"))
            throw new Exception("Editor memory policy must not force GC or trim the working set.");
        var environment = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorEnvironment");
        var syncCalls = environment
            .Methods.Single(m => m.Name == "Sync")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        if (syncCalls.Any(m => m.DeclaringType.Name == "Time"))
            throw new Exception("Terrain discovery must not poll on a timer.");
        foreach (var (method, call) in new[] { (".ctor", "add_sceneLoaded"), ("Dispose", "remove_sceneLoaded") })
            if (
                !environment
                    .Methods.Single(m => m.Name == method)
                    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == call)
            )
                throw new Exception("Terrain discovery must follow scene lifecycle: " + call);
        Console.WriteLine(
            "Editor memory: native GC wrapper, no forced collections, and scene-triggered terrain discovery verified offline."
        );
    }
}
