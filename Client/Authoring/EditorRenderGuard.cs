using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

// Native effects downsample to quarter resolution without checking small frames.
// Guard after SSAA replaces Unity's source, and branch to its normal release tail.
internal static class EditorRenderGuard
{
    private static bool _reported;

    internal static void Enable()
    {
        var harmony = new Harmony("com.wtt.campaigns.editor.rendering");
        foreach (
            var name in new[]
            {
                "UnityStandardAssets.ImageEffects.Bloom",
                "UnityStandardAssets.ImageEffects.BloomAndFlares",
                "PrismEffects",
            }
        )
        {
            var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
            var method =
                AccessTools.DeclaredMethod(type, "OnRenderImage", new[] { typeof(RenderTexture), typeof(RenderTexture) })
                ?? throw new MissingMethodException(name, "OnRenderImage");
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(EditorRenderGuard), nameof(BeforeEffect)),
                postfix: new HarmonyMethod(typeof(EditorRenderGuard), nameof(AfterEffect)),
                transpiler: new HarmonyMethod(typeof(EditorRenderGuard), nameof(Transpiler)));
        }
    }

    private static void BeforeEffect(out EditorDiagnostics.Scope __state) =>
        __state = EditorDiagnostics.Measure(EditorDiagnostics.Area.NativeEffects);

    private static void AfterEffect(EditorDiagnostics.Scope __state) => __state.Dispose();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = new List<CodeInstruction>(instructions);
        var fields = new List<int>();
        for (var i = 0; i < code.Count; i++)
            if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo { Name: "_ssaaPropagator" })
                fields.Add(i);
        if (fields.Count != 4)
            throw new InvalidOperationException("Unexpected native image-effect SSAA handoff.");
        // The first null-check skips source substitution; both paths meet here.
        var start = -1;
        for (var i = fields[0] + 1; i < fields[1]; i++)
            if (code[i].opcode.FlowControl == FlowControl.Cond_Branch && code[i].operand is Label label)
                start = code.FindIndex(c => Labels(c).Contains(label));
        var end = fields[2] - 1;
        if (start <= fields[1] || start >= end || code[end].opcode != OpCodes.Ldarg_0)
            throw new InvalidOperationException("Unexpected native image-effect cleanup boundary.");
        var cleanup = generator.DefineLabel();
        Labels(code[end]).Add(cleanup);
        var guard = new CodeInstruction(OpCodes.Ldarg_0);
        guard.MoveLabelsFrom(code[start]).MoveBlocksFrom(code[start]);
        code.InsertRange(
            start,
            new[]
            {
                guard,
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(EditorRenderGuard).GetMethod(nameof(Render), BindingFlags.NonPublic | BindingFlags.Static)
                ),
                new CodeInstruction(OpCodes.Brfalse, cleanup),
            }
        );
        return code;
    }

    // BepInEx Harmony exposes Mono mscorlib labels; use the non-generic list
    // across the netstandard reference boundary during patch construction only.
    private static IList Labels(CodeInstruction instruction) => (IList)typeof(CodeInstruction).GetField("labels")!.GetValue(instruction);

    private static bool Render(MonoBehaviour effect, RenderTexture source, RenderTexture destination)
    {
        var downsample = effect is PrismEffects prism && prism.useBloom ? prism.bloomDownsample : 4;
        if (EditorRenderSize.CanProcess(EditorMode.Active, source ? source.width : 0, source ? source.height : 0, downsample))
            return true;
        if (source && source != destination)
            Graphics.Blit(source, destination);
        if (!_reported)
        {
            _reported = true;
            Plugin.LogInfo(
                $"Editor rendering: bypassed undersized image effects on {effect.name} ({(source ? source.width : 0)}x{(source ? source.height : 0)}); normal frames retain native effects."
            );
        }
        return false;
    }
}
