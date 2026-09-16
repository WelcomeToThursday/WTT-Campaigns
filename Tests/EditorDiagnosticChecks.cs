using Mono.Cecil;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorDiagnosticChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var timing = new EditorTiming();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100000; i++)
            timing.Add(10, 20);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(
            allocated == 0 && timing.Calls == 100000 && timing.Ticks == 1000000 && timing.Allocated == 2000000,
            "Diagnostic accumulation uses bounded value storage and allocates nothing over 100,000 samples."
        );
        timing.Add(100, -1);
        check(
            timing.MaxTicks == 100 && timing.Allocated == 2000000,
            "Diagnostics preserve the longest stall without negative allocation counts."
        );
    }

    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        using var core = AssemblyDefinition.ReadAssembly(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(native.MainModule.FileName)!, "UnityEngine.CoreModule.dll"))
        );
        var diagnostics = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorDiagnostics");
        var calls = diagnostics
            .Methods.Concat(diagnostics.NestedTypes.SelectMany(t => t.Methods))
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        if (
            calls.Any(m =>
                m.Name
                    is "Collect"
                        or "CollectIncremental"
                        or "set_GCMode"
                        or "set_enabled"
                        or "TakeSnapshot"
                        or "FindObjectsOfTypeAll"
                        or "Kill"
                        or "Start"
                && m.DeclaringType.FullName == "System.Diagnostics.Process"
            )
        )
            throw new InvalidOperationException("Editor diagnostics must remain passive and avoid scene enumeration.");
        foreach (
            var name in new[]
            {
                "GetMonoUsedSizeLong",
                "GetMonoHeapSizeLong",
                "GetTotalAllocatedMemoryLong",
                "GetTotalReservedMemoryLong",
                "GetAllocatedMemoryForGraphicsDriver",
                "get_GCMode",
                "StartNew",
            }
        )
        {
            var call = calls.First(m => m.Name == name);
            var target = core.MainModule.GetType(call.DeclaringType.FullName);
            if (
                target == null
                || !target.Methods.Any(m =>
                    m.Name == name
                    && m.IsPublic
                    && m.IsStatic
                    && m.ReturnType.FullName == call.ReturnType.FullName
                    && m.Parameters.Select(p => p.ParameterType.FullName)
                        .SequenceEqual(call.Parameters.Select(p => p.ParameterType.FullName))
                )
            )
                throw new InvalidOperationException("Missing installed Unity diagnostic API: " + name);
        }
        if (!calls.Any(m => m.Name == "Dispose" && m.DeclaringType.Name == "ProfilerRecorder"))
            throw new InvalidOperationException("Editor diagnostics must release native recorders.");
        Console.WriteLine("Editor diagnostics: installed Unity counters, read-only sampling and recorder cleanup verified offline.");
    }
}
