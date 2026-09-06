using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SeasonalPerks.Tests;

internal static class ClientHookChecks
{
    // Exercise the real transpiler against real game instructions without starting EFT.
    internal static void Run(string sptRoot, string clientPath, bool bush = false, bool experience = false)
    {
        sptRoot = Path.GetFullPath(sptRoot);
        clientPath = Path.GetFullPath(clientPath);
        var folders = new[]
        {
            Path.GetDirectoryName(clientPath)!,
            Path.Combine(sptRoot, "BepInEx/DumpedAssemblies/EscapeFromTarkov"),
            Path.Combine(sptRoot, "BepInEx/core"),
            Path.Combine(sptRoot, "BepInEx/plugins/spt"),
            Path.Combine(sptRoot, "EscapeFromTarkov_Data/Managed"),
        };
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = folders.Select(f => Path.Combine(f, name.Name + ".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        };
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(folders[1], "Assembly-CSharp.dll"));
        var client = AssemblyLoadContext.Default.LoadFromAssemblyPath(clientPath);
        var harmony = Assembly.Load("0Harmony");
        var instructionType = harmony.GetType("HarmonyLib.CodeInstruction")!;
        var opCodes = typeof(System.Reflection.Emit.OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);
        using var assembly = AssemblyDefinition.ReadAssembly(game.Location);
        var patch = client.GetType(
            experience ? "SeasonalPerks.Client.Patches.Skills.RaidExperiencePatch"
            : bush ? "SeasonalPerks.Client.Patches.Movement.BushSoundPatch"
            : "SeasonalPerks.Client.Patches.Items.ItemResourcePatch"
        )!;
        var transpiler = patch.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (
            var (owner, methodName) in experience ? new[] { ("BaseStatisticsManager", "EndStatisticsSession") }
            : bush
                ? new[]
                {
                    ("TreeInteractive", "OnTriggerEnter"),
                    ("TreeInteractive", "IPhysicsTriggerWithStay.OnTriggerStay"),
                    ("TreeInteractive", "PlaySoundBank"),
                }
            : new[]
            {
                ("ActiveHealthController", "RegularUpdate"),
                ("ActiveHealthController", "Residue"),
                ("OfflineHealthController", "Started"),
            }
        )
        {
            var type = game.GetType(
                experience ? "EFT.BaseStatisticsManager"
                : bush ? "EFT.Interactive.TreeInteractive"
                : "EFT.HealthSystem." + owner + "+MedEffect"
            )!;
            var target = experience
                ? type.GetMethod(methodName, new[] { game.GetType("EFT.ExitStatus")!, typeof(float) })!
                : type.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )!;
            var definition = assembly
                .MainModule.GetTypes()
                .Single(t => t.FullName == type.FullName!.Replace('+', '/'))
                .Methods.Single(m => m.Name == methodName && (!experience || m.Parameters.Count == 2));
            var instructions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instructionType))!;
            var generator = new DynamicMethod("clientHookCheck", typeof(void), Type.EmptyTypes).GetILGenerator();
            var labels = definition.Body.Instructions.ToDictionary(i => i, _ => generator.DefineLabel());
            foreach (var instruction in definition.Body.Instructions)
            {
                object? operand = instruction.Operand switch
                {
                    // The dump publicizer unseals EFT delegate types, which CoreCLR
                    // refuses to resolve. This transpiler only matches the bonus field;
                    // preserve other metadata operands without loading their owners.
                    MemberReference m when experience && m.Name != "ExperienceBonusMult" => m,
                    MethodReference m => game.ManifestModule.ResolveMethod(m.MetadataToken.ToInt32()),
                    FieldReference f => game.ManifestModule.ResolveField(f.MetadataToken.ToInt32()),
                    TypeReference t => game.ManifestModule.ResolveType(t.MetadataToken.ToInt32()),
                    Instruction branch => labels[branch],
                    Instruction[] branches => branches.Select(b => labels[b]).ToArray(),
                    VariableDefinition variable => variable.Index,
                    ParameterDefinition parameter => parameter.Index + 1,
                    _ => instruction.Operand,
                };
                var converted = Activator.CreateInstance(instructionType, opCodes[instruction.OpCode.Value], operand)!;
                ((IList)instructionType.GetField("labels")!.GetValue(converted)!).Add(labels[instruction]);
                instructions.Add(converted);
            }
            var transformed = (IEnumerable)transpiler.Invoke(null, new object[] { instructions, target })!;
            var result = transformed.Cast<object>().ToArray(); // Execute all shape guards.
            var definedLabels = result
                .SelectMany(i => ((IEnumerable)instructionType.GetField("labels")!.GetValue(i)!).Cast<Label>())
                .ToArray();
            if (
                definedLabels.Length != labels.Count
                || definedLabels.Distinct().Count() != labels.Count
                || labels.Values.Except(definedLabels).Any()
            )
            {
                throw new Exception("Client patch lost or duplicated branch labels: " + target);
            }

            var storeIndex = instructions
                .Cast<object>()
                .Select((instruction, index) => (instruction, index))
                .First(pair =>
                    (System.Reflection.Emit.OpCode)instructionType.GetField("opcode")!.GetValue(pair.instruction)!
                        == (bush ? System.Reflection.Emit.OpCodes.Ldfld : System.Reflection.Emit.OpCodes.Stfld)
                    && instructionType.GetField("operand")!.GetValue(pair.instruction) is FieldInfo field
                    && field.Name
                        == (
                            experience ? "ExperienceBonusMult"
                            : bush ? "Rolloff"
                            : "HpResource"
                        )
                )
                .index;
            instructions.RemoveAt(storeIndex);
            var rejected = false;
            try
            {
                _ = ((IEnumerable)transpiler.Invoke(null, new object[] { instructions, target })!).Cast<object>().ToArray();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new Exception("Client patch accepted a missing consumption site: " + target);
            }

            Console.WriteLine(
                $"PASS actual {(experience ? "experience" : bush ? "bush" : "resource")} transpiler: {owner}.{methodName} ({result.Length} instructions; labels preserved; missing site rejected)"
            );
        }
    }
}
