using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class EditorLoadingChecks
{
    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        void Require(bool valid, string message)
        {
            if (!valid)
                throw new InvalidOperationException("Editor loading: " + message);
        }
        var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorMode");
        var open = editor.Methods.Single(m => m.Name == "OpenMap");
        var openMachine = (
            (TypeReference)
                open.CustomAttributes.Single(a => a.AttributeType.Name == "AsyncStateMachineAttribute").ConstructorArguments[0].Value
        ).Resolve();
        var loading = openMachine.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.ToList();
        var clock = loading.FindIndex(i => i.Operand is MethodReference m && m.Name == "set_MatchingStartTime");
        var match = loading.FindIndex(i => i.Operand is MethodReference m && m.Name == "LocalGameMatching");
        Require(
            clock >= 0
                && clock < match
                && loading[clock - 1].Operand is MethodReference { Name: "get_Now", DeclaringType.Name: "DateTimeExtensions" },
            "Every map load must initialize the native clock before its loading screen opens."
        );
        var screenType = native.MainModule.GetType("EFT.UI.Matchmaker.MatchmakerTimeHasCome");
        Require(
            screenType
                .Methods.Single(m => m.Name == "Show" && m.Parameters.Count == 3)
                .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "get_MatchingStartTime" }),
            "Loading screen reads the initialized matching clock."
        );
        Require(
            editor
                .Methods.Single(m => m.Name == "Awake")
                .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Enable", DeclaringType.Name: "EditorDeployment" }),
            "Deployment guard is registered at startup."
        );

        var routine = native.MainModule.GetType("EFT.LocalGame").Methods.Single(m => m.Name == "vmethod_2");
        var machine = (
            (TypeReference)
                routine.CustomAttributes.Single(a => a.AttributeType.Name == "IteratorStateMachineAttribute").ConstructorArguments[0].Value
        ).Resolve();
        var body = machine.Methods.Single(m => m.Name == "MoveNext").Body.Instructions;
        var context = new ClientAssemblyContext(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(native.MainModule.FileName)!, "../../..")),
            client.MainModule.FileName
        );
        var game = context.LoadFromAssemblyName(new AssemblyName("Assembly-CSharp"));
        var compiled = context.LoadFromAssemblyPath(client.MainModule.FileName);
        var harmony = context.LoadFromAssemblyName(new AssemblyName("0Harmony"));
        var instructionType = harmony.GetType("HarmonyLib.CodeInstruction")!;
        var operandField = instructionType.GetField("operand")!;
        var labelsField = instructionType.GetField("labels")!;
        var patch = compiled.GetType("WTT.Campaigns.Client.Authoring.EditorDeployment")!;
        var transpiler = patch.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!;
        var opcodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);
        IList Input(bool includeDelay = true, bool includeScreen = true)
        {
            var input = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instructionType))!;
            var generator = new DynamicMethod("editorLoadingCheck", typeof(void), Type.EmptyTypes).GetILGenerator();
            foreach (var instruction in body)
            {
                object? operand = instruction.Operand;
                if (operand is FieldReference { Name: "TimeBeforeDeployLocal" } f)
                {
                    if (!includeDelay)
                        continue;
                    operand = game.ManifestModule.ResolveField(f.MetadataToken.ToInt32());
                }
                if (operand is MethodReference { Name: "ShowScreen" } m)
                {
                    if (!includeScreen)
                        continue;
                    operand = game.ManifestModule.ResolveMethod(m.MetadataToken.ToInt32());
                }
                var converted = Activator.CreateInstance(instructionType, opcodes[instruction.OpCode.Value], operand)!;
                ((IList)labelsField.GetValue(converted)!).Add(generator.DefineLabel());
                input.Add(converted);
            }
            return input;
        }
        var source = Input();
        var output = ((IEnumerable)transpiler.Invoke(null, new object[] { source })!).Cast<object>().ToArray();
        Require(
            output.Length == source.Count + 1 && output.Where(source.Contains).SequenceEqual(source.Cast<object>()),
            "Deployment patch preserves all native setup instructions and coroutine completion."
        );
        Require(
            output.Count(i => operandField.GetValue(i) is MethodInfo { Name: "Delay" }) == 1
                && output.Count(i => operandField.GetValue(i) is MethodInfo { Name: "Show" }) == 1,
            "Only the delay and deployment screen transition are redirected."
        );
        Require(source.Cast<object>().All(i => ((IList)labelsField.GetValue(i)!).Count == 1), "Native branch labels survive the patch.");
        foreach (var input in new[] { Input(false, true), Input(true, false), Input(false, false) })
        {
            var rejected = false;
            try
            {
                _ = ((IEnumerable)transpiler.Invoke(null, new object[] { input })!).Cast<object>().ToArray();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected, "A missing native deployment site must fail validation.");
        }
        var deployment = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.EditorDeployment");
        foreach (var name in new[] { "Delay", "Show" })
            Require(
                deployment
                    .Methods.Single(m => m.Name == name)
                    .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "get_LoadingMap" }),
                "Deployment bypass must be limited to the authenticated editor map load: " + name
            );
        Require(
            deployment
                .Methods.Single(m => m.Name == "Show")
                .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "CloseScreen" }),
            "Skipping countdown still closes the loading controller before native MenuUI teardown."
        );
        Console.WriteLine(
            "Editor loading: native timer initialization and actual deployment transpiler passed offline; setup and labels preserved, missing sites rejected."
        );
    }
}
