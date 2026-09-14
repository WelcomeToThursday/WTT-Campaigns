using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WTT.Campaigns.Client.Authoring;
using FlowControl = Mono.Cecil.Cil.FlowControl;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace WTT.Campaigns.Tests;

internal static class EditorRenderChecks
{
    internal static void Sizes(Action<bool, string> check)
    {
        foreach (var (width, height) in new[] { (0, 0), (1, 1), (3, 1080), (1920, 3) })
        {
            check(!EditorRenderSize.CanProcess(true, width, height), "Editor rejects undersized image-effect frames.");
            check(EditorRenderSize.CanProcess(false, width, height), "Normal gameplay rendering remains native.");
        }
        foreach (var (width, height) in new[] { (4, 4), (1280, 1024), (1920, 1080), (2560, 1440), (3440, 1440) })
            check(EditorRenderSize.CanProcess(true, width, height), "Valid editor frames retain image effects.");
        check(!EditorRenderSize.CanProcess(true, 4, 4, 8), "Prism's configured downsample must also fit.");
        check(EditorRenderSize.CanProcess(true, 8, 8, 8), "Prism downsample boundary is valid.");
    }

    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        // Validate the branch boundaries used by the transpiler against the actual
        // installed methods, including the effect which has no separate inner body.
        var nativePath = native.MainModule.FileName;
        var firstpassPath = Path.Combine(Path.GetDirectoryName(nativePath)!, "Assembly-CSharp-firstpass.dll");
        if (!File.Exists(firstpassPath))
            firstpassPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(nativePath)!, "../../../EscapeFromTarkov_Data/Managed/Assembly-CSharp-firstpass.dll")
            );
        using var firstpass = AssemblyDefinition.ReadAssembly(firstpassPath);
        var context = new ClientAssemblyContext(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(firstpassPath)!, "../..")),
            client.MainModule.FileName
        );
        var compiled = context.LoadFromAssemblyPath(client.MainModule.FileName);
        var harmony = context.LoadFromAssemblyName(new AssemblyName("0Harmony"));
        foreach (
            var type in new[]
            {
                firstpass.MainModule.GetType("UnityStandardAssets.ImageEffects.Bloom"),
                firstpass.MainModule.GetType("UnityStandardAssets.ImageEffects.BloomAndFlares"),
                native.MainModule.GetType("PrismEffects"),
            }
        )
        {
            var code = type.Methods.Single(m => m.Name == "OnRenderImage").Body.Instructions;
            var fields = code.Where(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference { Name: "_ssaaPropagator" }).ToArray();
            if (fields.Length != 4)
                throw new InvalidOperationException("Unexpected SSAA handoff: " + type.Name);
            var branch = code.Skip(code.IndexOf(fields[0]) + 1)
                .TakeWhile(i => i != fields[1])
                .Single(i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
            var start = (Instruction)branch.Operand;
            var cleanup = fields[2].Previous;
            if (start.Offset <= fields[1].Offset || start.Offset >= cleanup.Offset || cleanup.OpCode != OpCodes.Ldarg_0)
                throw new InvalidOperationException("Invalid editor render guard boundaries: " + type.Name);
            if (
                code.Where(i => i.Offset >= cleanup.Offset).Any(i => i.OpCode.Code.ToString().StartsWith("Ldloc", StringComparison.Ordinal))
            )
                throw new InvalidOperationException("Cleanup requires uninitialized effect locals: " + type.Name);
            if (
                !code.Where(i => i.Offset >= cleanup.Offset)
                    .Any(i => i.Operand is MethodReference m && m.Name.StartsWith("ReleaseSourceDestination", StringComparison.Ordinal))
            )
                throw new InvalidOperationException("Missing SSAA cleanup: " + type.Name);
            Actual(context, compiled, harmony, type, start, cleanup);
        }
        var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorMode");
        if (
            !editor
                .Methods.Single(m => m.Name == "Awake")
                .Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.DeclaringType.Name == "EditorRenderGuard" && m.Name == "Enable"
                )
        )
            throw new InvalidOperationException("Editor rendering guards must register before startup.");
        Console.WriteLine("Editor rendering: all three native effect handoffs and cleanup branches verified offline.");
    }

    private static void Actual(
        ClientAssemblyContext context,
        Assembly compiled,
        Assembly harmony,
        TypeDefinition type,
        Instruction start,
        Instruction cleanup
    )
    {
        var game = context.LoadFromAssemblyName(new AssemblyName(type.Module.Assembly.Name.Name));
        var instructionType = harmony.GetType("HarmonyLib.CodeInstruction")!;
        var patch = compiled.GetType("WTT.Campaigns.Client.Authoring.EditorRenderGuard")!;
        var transpiler = patch.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!;
        var labelsField = instructionType.GetField("labels")!;
        var operandField = instructionType.GetField("operand")!;
        var opcodeField = instructionType.GetField("opcode")!;
        var opcodes = typeof(System.Reflection.Emit.OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);
        var code = type.Methods.Single(m => m.Name == "OnRenderImage").Body.Instructions;
        var generator = new DynamicMethod("editorRenderCheck", typeof(void), Type.EmptyTypes).GetILGenerator();
        var labels = code.ToDictionary(i => i, _ => generator.DefineLabel());
        var input = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instructionType))!;
        foreach (var instruction in code)
        {
            object? operand = instruction.Operand switch
            {
                FieldReference { Name: "_ssaaPropagator" } f => game.ManifestModule.ResolveField(f.MetadataToken.ToInt32()),
                Instruction branch => labels[branch],
                Instruction[] branches => branches.Select(b => labels[b]).ToArray(),
                _ => instruction.Operand,
            };
            var converted = Activator.CreateInstance(instructionType, opcodes[instruction.OpCode.Value], operand)!;
            ((IList)labelsField.GetValue(converted)!).Add(labels[instruction]);
            input.Add(converted);
        }
        var output = ((IEnumerable)transpiler.Invoke(null, new object[] { input, generator })!).Cast<object>().ToArray();
        var defined = output.SelectMany(i => ((IEnumerable)labelsField.GetValue(i)!).Cast<Label>()).ToArray();
        if (
            output.Length != input.Count + 5
            || defined.Length != labels.Count + 1
            || defined.Distinct().Count() != defined.Length
            || labels.Values.Except(defined).Any()
        )
            throw new InvalidOperationException("Render guard lost native instructions or labels: " + type.Name);
        var guardStart = Array.FindIndex(output, i => ((IList)labelsField.GetValue(i)!).Contains(labels[start]));
        var cleanupIndex = Array.FindIndex(output, i => ((IList)labelsField.GetValue(i)!).Contains(labels[cleanup]));
        if (
            guardStart != code.IndexOf(start)
            || operandField.GetValue(output[guardStart + 3]) is not MethodInfo { Name: "Render" }
            || !opcodeField.GetValue(output[guardStart + 4])!.Equals(System.Reflection.Emit.OpCodes.Brfalse)
            || !((IList)labelsField.GetValue(output[cleanupIndex])!).Contains(operandField.GetValue(output[guardStart + 4]))
        )
            throw new InvalidOperationException("Guard must run after SSAA substitution and bypass directly to cleanup: " + type.Name);
        var original = output.Where(i => input.Contains(i)).ToArray();
        if (!original.SequenceEqual(input.Cast<object>()))
            throw new InvalidOperationException("Native effect body changed: " + type.Name);
        // Reject a changed native method rather than silently installing a partial guard.
        input.Remove(input.Cast<object>().First(i => operandField.GetValue(i) is FieldInfo { Name: "_ssaaPropagator" }));
        try
        {
            transpiler.Invoke(null, new object[] { input, generator });
            throw new Exception("Unsupported rendering method was accepted.");
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        Console.WriteLine("PASS actual editor render transpiler: " + type.Name + " (labels, bypass, native body, cleanup and rejection)");
    }
}
