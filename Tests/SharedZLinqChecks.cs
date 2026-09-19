using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

namespace WTT.Campaigns.Tests;

// Inspect every emitted reference, then execute real Shared code with each host's DLL.
// This never loads a game/server application or copies files into either runtime.
internal static class SharedZLinqChecks
{
    internal static void Run(string sharedPath, string clientLibrary, string serverLibrary)
    {
        sharedPath = Path.GetFullPath(sharedPath);
        using var shared = AssemblyDefinition.ReadAssembly(sharedPath);
        Check(
            !shared
                .MainModule.GetMemberReferences()
                .OfType<MethodReference>()
                .Any(m => m.DeclaringType.FullName == "System.Linq.Enumerable"),
            "Shared queries use ZLinq throughout, including nested lambdas and iterators"
        );
        var requested = shared.MainModule.AssemblyReferences.Single(r => r.Name == "ZLinq");
        Check(requested.Version == new Version(1, 5, 3, 0), "Shared retains the 1.5.3 compile baseline");
        foreach (var type in shared.MainModule.GetTypes())
        {
            // The server's net10 ZLinq enumerators are ref structs. A netstandard
            // iterator/closure field or a Shared generic helper cannot retain them.
            foreach (var field in type.Fields)
                Check(!IsZLinq(field.FieldType), "Shared must not store ZLinq enumerators in fields: " + field.FullName);
            foreach (var parameter in type.GenericParameters.Concat(type.Methods.SelectMany(m => m.GenericParameters)))
                Check(
                    !parameter.Constraints.Any(c => UsesZLinq(c.ConstraintType)),
                    "Shared generic constraints must not capture host-specific ZLinq enumerators: " + parameter.FullName
                );
        }
        foreach (var type in shared.MainModule.GetTypes().Where(t => t.IsPublic || t.IsNestedPublic))
        {
            foreach (var method in type.Methods.Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
                Check(
                    !UsesZLinq(method.ReturnType) && method.Parameters.All(p => !UsesZLinq(p.ParameterType)),
                    "Shared API does not expose ZLinq: " + method.FullName
                );
            foreach (var field in type.Fields.Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                Check(!UsesZLinq(field.FieldType), "Shared field does not expose ZLinq: " + field.FullName);
        }

        foreach (var libraryPath in new[] { clientLibrary, serverLibrary }.Select(Path.GetFullPath))
        {
            using var library = AssemblyDefinition.ReadAssembly(libraryPath);
            Check(library.Name.Name == requested.Name, "Host library is ZLinq");
            Check(library.Name.PublicKeyToken.SequenceEqual(requested.PublicKeyToken), "Host signing identity matches Shared");
            Check(library.Name.Version >= requested.Version, "Host meets Shared's assembly version floor");
            using var resolver = new HostResolver(library);
            using var candidate = AssemblyDefinition.ReadAssembly(sharedPath, new ReaderParameters { AssemblyResolver = resolver });
            var references = candidate.MainModule.GetMemberReferences().Where(r => IsZLinq(r.DeclaringType)).ToArray();
            Check(references.Length > 0, "Compiled Shared actually uses ZLinq");
            foreach (var reference in candidate.MainModule.GetTypeReferences().Where(IsZLinq))
                Check(reference.Resolve()?.Module == library.MainModule, "Host supplies type " + reference.FullName);
            foreach (var reference in references)
            {
                var resolved = reference switch
                {
                    MethodReference method => (IMemberDefinition?)method.Resolve(),
                    FieldReference field => field.Resolve(),
                    _ => throw new InvalidOperationException("Unhandled ZLinq reference: " + reference),
                };
                Check(resolved?.DeclaringType.Module == library.MainModule, "Host supplies member " + reference.FullName);
            }

            // Prove the guard detects a missing API, even when assembly identity still matches.
            var call = references.OfType<MethodReference>().First();
            var definition = call.Resolve();
            var owner = definition.DeclaringType;
            owner.Methods.Remove(definition);
            Check(call.Resolve() == null, "Compatibility guard rejects a removed host method");
            owner.Methods.Add(definition);

            Exercise(sharedPath, libraryPath);
            Console.WriteLine($"Shared ZLinq compatibility passed: {library.Name.FullName}; {references.Length} member references.");
        }
    }

    private static bool IsZLinq(TypeReference type) => type.GetElementType().Scope?.Name == "ZLinq";

    private static bool UsesZLinq(TypeReference type) =>
        IsZLinq(type)
        || (type is GenericInstanceType generic && generic.GenericArguments.Any(UsesZLinq))
        || (type is TypeSpecification specification && UsesZLinq(specification.ElementType));

    private static void Exercise(string sharedPath, string libraryPath)
    {
        var context = new HostContext(libraryPath);
        try
        {
            var library = context.LoadFromAssemblyPath(libraryPath);
            var shared = context.LoadFromAssemblyPath(sharedPath);
            ExerciseStreaming(shared);
            Type Type(string name) => shared.GetType("WTT.Campaigns.Shared.Hub." + name, throwOnError: true)!;
            object? Call(string type, string method, params object[] args) => Type(type).GetMethod(method)!.Invoke(null, args);
            var raid = Activator.CreateInstance(Type("HubRaid"))!;
            var progress = Activator.CreateInstance(Type("HubProgress"))!;
            Call("HubProvenance", "Register", raid, "source", "template", 3, true);
            Call("HubProvenance", "Transfer", raid, "source", "split", 2, true);
            var stacks = (System.Collections.IDictionary)Type("HubRaid").GetProperty("Stacks")!.GetValue(raid)!;
            List<string> Units(string id) => (List<string>)stacks[id]!.GetType().GetProperty("Units")!.GetValue(stacks[id])!;
            Check(Units("source").SequenceEqual(new[] { "source:2" }), "Transfer retains the remaining source unit");
            Check(Units("split").SequenceEqual(new[] { "source:0", "source:1" }), "Transfer conserves ordered unit identities");
            Call("HubProvenance", "Transfer", raid, "split", "source", 1, false);
            Check(Units("source").SequenceEqual(new[] { "source:2", "source:0" }), "Transfer appends without replacing destination units");

            Call("HubRules", "Pickup", progress, raid, "source:0", 1L, 30, 1380);
            var rolls = 0;
            Func<int> roll = () =>
            {
                rolls++;
                return 0;
            };
            var extracted = new[] { "source:0", "source:0", "untracked" };
            Check(
                (int)Call("HubRules", "Finish", progress, raid, extracted, true, roll, 5)! == 1 && rolls == 1,
                "Distinct extraction awards each tracked unit once"
            );
            Check(
                (int)Call("HubRules", "Finish", progress, raid, extracted, true, roll, 5)! == 1 && rolls == 1,
                "Repeated finalization does not award again"
            );
            var costs = new Dictionary<string, int>
            {
                ["a"] = 5,
                ["b"] = 2,
                ["c"] = 3,
            };
            var owned = new Dictionary<string, int> { ["a"] = 2, ["b"] = 9 };
            Check((int)Call("HubRules", "Shortage", costs, owned)! == 6, "Sum preserves shortage and surplus behavior");
            Check((int)Call("HubRules", "Shortage", new Dictionary<string, int>(), owned)! == 0, "Empty costs have no shortage");
            Check(
                context.Assemblies.Single(a => a.GetName().Name == "ZLinq") == library,
                "Shared executed with the selected host library, without fallback to the test package"
            );
        }
        finally
        {
            context.Unload();
        }
    }

    private static void ExerciseStreaming(Assembly shared)
    {
        var zoneType = shared.GetType("WTT.Campaigns.Shared.Spatial.SeasonZone")!;
        var trackerType = typeof(TrackedSequence<>).MakeGenericType(zoneType);
        var tracker = Activator.CreateInstance(trackerType)!;
        var values = (System.Collections.IList)trackerType.GetField("Values")!.GetValue(tracker)!;
        object Zone()
        {
            var zone = Activator.CreateInstance(zoneType)!;
            zoneType.GetProperty("LayoutId")!.SetValue(zone, "layout");
            return zone;
        }
        values.Add(Zone());
        values.Add(Zone());
        var query = (System.Collections.IEnumerable)
            shared
                .GetType("WTT.Campaigns.Shared.Spatial.ZoneLayoutRules")!
                .GetMethod("ForEditor")!
                .Invoke(null, new[] { tracker, "layout", (object)false })!;
        int Count(string name) => (int)trackerType.GetField(name)!.GetValue(tracker)!;
        Check(Count("Starts") == 0, "Public IEnumerable queries preserve deferred execution");
        values.Add(Zone());
        var sequence = query.Cast<object>();
        Check(sequence.SequenceEqual(values.Cast<object>()), "Deferred query observes source changes before enumeration");
        Check(
            sequence.SequenceEqual(values.Cast<object>()) && Count("Starts") == 2 && Count("Disposals") == 2,
            "Repeated enumeration starts fresh and disposes each source enumerator"
        );
        foreach (var item in sequence)
        {
            Check(ReferenceEquals(item, values[0]), "Early exit reads the first item");
            break;
        }
        Check(Count("Disposals") == 3, "Early exit disposes the source enumerator");
        trackerType.GetField("Fail")!.SetValue(tracker, true);
        var failed = false;
        try
        {
            foreach (var item in sequence)
                _ = item;
        }
        catch (InvalidOperationException error) when (error.Message == "enumeration sentinel")
        {
            failed = true;
        }
        Check(failed && Count("Disposals") == 4, "Enumeration failure propagates and disposes the source");
    }

    private sealed class TrackedSequence<T> : IEnumerable<T>
    {
        public readonly List<T> Values = new();
        public int Starts;
        public int Disposals;
        public bool Fail;

        public IEnumerator<T> GetEnumerator()
        {
            Starts++;
            try
            {
                foreach (var value in Values)
                {
                    if (Fail)
                        throw new InvalidOperationException("enumeration sentinel");
                    yield return value;
                }
            }
            finally
            {
                Disposals++;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HostResolver(AssemblyDefinition library) : DefaultAssemblyResolver
    {
        public override AssemblyDefinition Resolve(AssemblyNameReference name) => name.Name == "ZLinq" ? library : base.Resolve(name);
    }

    private sealed class HostContext(string libraryPath) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name == "ZLinq" ? LoadFromAssemblyPath(libraryPath) : null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
