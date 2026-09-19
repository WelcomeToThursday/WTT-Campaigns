using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class EditorSceneVisibilityChecks
{
    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        void Require(bool valid, string message)
        {
            if (!valid)
                throw new InvalidOperationException("Editor visibility: " + message);
        }
        var types = native.MainModule.GetTypes().ToDictionary(t => t.FullName);
        var bake = types["Koenigz.PerfectCulling.PerfectCullingBakeGroup"];
        var desired = bake.Properties.Single(p => p.Name == "IsEnabled").SetMethod.Body.Instructions;
        Require(
            desired.Any(i => i.Operand is FieldReference { Name: "isGroupEnabled" })
                && desired.Any(i => i.Operand is MethodReference { Name: "Toggle" }),
            "Native requested state is stored separately from visible rendering."
        );
        var query = types["Koenigz.PerfectCulling.EFT.CullingGridVisibilityQueryResult"];
        Require(
            query
                .Methods.Single(m => m.Name == "TestAABB")
                .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "get_IsEmpty" }),
            "Native visibility rejects objects when the free camera has no baked visibility cell."
        );
        var compiledType = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Rendering.EditorSceneVisibility");
        var dispose = compiledType.Methods.Single(m => m.Name == "Dispose").Body.Instructions;
        Require(
            dispose.Any(i => i.Operand is MethodReference { Name: "get_IsEnabled" })
                && dispose.Any(i => i.Operand is MethodReference { Name: "Toggle" }),
            "Closing restores the latest native requested state."
        );
        Require(
            !compiledType
                .Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i => i.Operand is MethodReference m && m.Name is "SetActive" or "set_IsEnabled" or "ForceLOD"),
            "Visibility bypass preserves authored hidden objects, requested culling state and native LOD selection."
        );
        var environment = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Rendering.EditorEnvironment");
        var advance = compiledType.Methods.Single(m => m.Name == "Advance").Body.Instructions;
        Require(
            advance.Any(i => i.Operand is MethodReference { Name: "get_frameCount" })
                && advance.Any(i => i.Operand is MethodReference { Name: "Advance", DeclaringType.Name: "EditorTriggerVisibility" })
                && environment
                    .Methods.Single(m => m.Name == "Sync")
                    .Body.Instructions.Any(i =>
                        i.Operand is MethodReference { Name: "Advance", DeclaringType.Name: "EditorSceneVisibility" }
                    ),
            "Visibility discovery advances under a shared per-frame budget across native camera callbacks."
        );
        Require(
            environment
                .Methods.Single(m => m.Name == "Dispose")
                .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Dispose", DeclaringType.Name: "EditorSceneVisibility" }),
            "Environment teardown releases the visibility bypass."
        );
        var trigger = types["DisablerCullingObject"];
        var switchBody = trigger.Methods.Single(m => m.Name == "SetComponentsEnabled").Body.Instructions;
        Require(
            switchBody.Count(i => i.Operand is MethodReference { Name: "SetComponentsEnabled", DeclaringType.Name: "CustomCullingCommon" })
                == 2
                && switchBody.Any(i => i.Operand is FieldReference { Name: "_enteredColliders" }),
            "Native trigger switches have two worker paths; the second ignores the requested visibility and reads player colliders."
        );
        foreach (var name in new[] { "_setComponentsEnabledWorker", "_setComponentsEnabledWorker2" })
            Require(
                trigger.Fields.Any(f => f.Name == name && f.IsPrivate && f.FieldType.FullName == "System.Collections.IEnumerator"),
                "Pending native hide worker can be stopped: " + name
            );
        var triggerLease = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Rendering.EditorTriggerVisibility");
        var calls = triggerLease
            .Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.Operand is MethodReference)
            .Select(i => (MethodReference)i.Operand)
            .ToArray();
        Require(
            calls.Any(m => m.Name == "StopCoroutine" && m.Parameters.Single().ParameterType.FullName == "System.Collections.IEnumerator")
                && calls.Any(m => m.Name == "IsEnabledUniversal")
                && calls.Any(m => m.Name == "SetEnabledUniversal")
                && calls.Any(m => m.Name == "ForceUpdate"),
            "Trigger lease uses exact native component and coroutine APIs and requests restoration."
        );
        Require(!calls.Any(m => m.Name is "StopAllCoroutines" or "ForceLOD"), "Unrelated workers and LOD selection are preserved.");
        var awake = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorMode").Methods.Single(m => m.Name == "Awake");
        Require(
            awake.Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Enable", DeclaringType.Name: "EditorSceneVisibility" }),
            "Culling hooks install before map loading can inline the native methods."
        );

        var context = new ClientAssemblyContext(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(native.MainModule.FileName)!, "../..")),
            client.MainModule.FileName
        );
        var compiled = context.LoadFromAssemblyPath(client.MainModule.FileName);
        var type = compiled.GetType(compiledType.FullName)!;
        var compiledTrigger = compiled.GetType(triggerLease.FullName)!;
        foreach (var name in new[] { "Worker", "InverseWorker" })
            Require(
                compiledTrigger.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is FieldInfo field
                    && field.DeclaringType!.Name == "DisablerCullingObject"
                    && field.FieldType == typeof(System.Collections.IEnumerator),
                "Actual compiled worker lookup resolves against the installed native assembly: " + name
            );
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var editing = type.GetField("_editing", flags)!;
        var active = type.GetField("_active", flags)!;
        var test = type.GetMethod("BeforeTest", flags)!;
        var toggle = type.GetMethod("BeforeToggle", flags)!;
        var groupType = context.LoadFromAssemblyName(new AssemblyName("Assembly-CSharp")).GetType(bake.FullName)!;
        var group = Activator.CreateInstance(groupType)!;
        var untouched = Activator.CreateInstance(groupType)!;
        var lease = RuntimeHelpers.GetUninitializedObject(type);
        var groupsType = typeof(HashSet<>).MakeGenericType(groupType);
        var groups = Activator.CreateInstance(groupsType)!;
        groupsType.GetMethod("Add")!.Invoke(groups, new[] { group });
        type.GetField("_groups", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(lease, groups);
        try
        {
            foreach (var enabled in new[] { false, true, false, true, false })
            {
                editing.SetValue(null, enabled);
                active.SetValue(null, enabled ? lease : null);
                var queryArgs = new object[] { false };
                Require(
                    (bool)test.Invoke(null, queryArgs)! == !enabled && (bool)queryArgs[0] == enabled,
                    "Grid bypass follows editor entry and exit, including consecutive sessions."
                );
                foreach (var target in new[] { group, untouched })
                foreach (var requested in new[] { false, true })
                {
                    groupType.GetField("isGroupEnabled")!.SetValue(target, requested);
                    var args = new[] { target, (object)requested };
                    toggle.Invoke(null, args);
                    Require(
                        (bool)args[1] == (requested || enabled && ReferenceEquals(target, group)),
                        "Only registered editor scene groups bypass baked hiding."
                    );
                    Require(
                        (bool)groupType.GetProperty("IsEnabled")!.GetValue(target)! == requested,
                        "Rendering overrides leave native requested visibility intact."
                    );
                }
            }
        }
        finally
        {
            editing.SetValue(null, false);
            active.SetValue(null, null);
        }
        Console.WriteLine(
            "Editor visibility: grid/baked guards, native trigger workers, both component paths and early hook registration verified offline; state restoration tested with managed doubles."
        );
    }
}
