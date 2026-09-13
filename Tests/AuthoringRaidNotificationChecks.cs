using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class AuthoringRaidNotificationChecks
{
    internal static void Run(string gamePath, string clientPath)
    {
        var context = new ClientAssemblyContext(gamePath, clientPath);
        var game = context.LoadFromAssemblyName(new AssemblyName("Assembly-CSharp"));
        var client = context.LoadFromAssemblyPath(Path.GetFullPath(clientPath));
        var harmony = context.LoadFromAssemblyName(new AssemblyName("0Harmony"));
        var instructionType = harmony.GetType("HarmonyLib.CodeInstruction")!;
        var patch = client.GetType("WTT.Campaigns.Client.Patches.Session.AuthoringRaidNotifications")!;
        var transpiler = patch.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!;
        var operandField = instructionType.GetField("operand")!;
        var opcodeField = instructionType.GetField("opcode")!;
        var labelsField = instructionType.GetField("labels")!;
        var opcodes = typeof(System.Reflection.Emit.OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);
        using var native = AssemblyDefinition.ReadAssembly(game.Location);
        var app = native.MainModule.GetType("EFT.TarkovApplication");
        foreach (
            var (name, call, replacement) in new[]
            {
                ("LocalGameCreate", "Deactivate", "EnterRaid"),
                ("OnGameEnd", "Activate", "LeaveRaid"),
            }
        )
        {
            var method = app.Methods.Single(m => m.Name == name);
            var machine = (TypeReference)
                method.CustomAttributes.Single(a => a.AttributeType.Name == "AsyncStateMachineAttribute").ConstructorArguments[0].Value;
            var body = machine.Resolve().Methods.Single(m => m.Name == "MoveNext").Body.Instructions;
            var input = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instructionType))!;
            var il = new DynamicMethod("raidNotifications", typeof(void), Type.EmptyTypes).GetILGenerator();
            object? target = null;
            var matched = 0;
            foreach (var source in body)
            {
                object? operand = source.Operand;
                if (operand is MethodReference m && m.DeclaringType.FullName == "EFT.Communications.NotificationManager" && m.Name == call)
                {
                    operand = game.ManifestModule.ResolveMethod(m.MetadataToken.ToInt32());
                    matched++;
                }
                var instruction = Activator.CreateInstance(instructionType, opcodes[source.OpCode.Value], operand)!;
                ((IList)labelsField.GetValue(instruction)!).Add(il.DefineLabel());
                input.Add(instruction);
                if (operand is MethodInfo)
                    target = instruction;
            }
            Require(matched == 1, name + " must have exactly one notification transition");
            var output = ((IEnumerable)transpiler.Invoke(null, new object[] { input })!).Cast<object>().ToArray();
            Require(
                output.Length == input.Count && output.Select((value, index) => ReferenceEquals(value, input[index])).All(v => v),
                "Transpiler preserves every native instruction, label and exception boundary"
            );
            Require(
                opcodeField.GetValue(target)!.Equals(System.Reflection.Emit.OpCodes.Call)
                    && operandField.GetValue(target) is MethodInfo { Name: var helper }
                    && helper == replacement,
                name + " routes its notification transition through " + replacement
            );
            input.Remove(target);
            var rejected = false;
            try
            {
                _ = ((IEnumerable)transpiler.Invoke(null, new object[] { input })!).Cast<object>().ToArray();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected, "Missing native transition must fail validation");
            Console.WriteLine(
                "PASS actual authoring notification transpiler: " + name + " (native instructions preserved; missing site rejected)"
            );
        }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
    }
}
