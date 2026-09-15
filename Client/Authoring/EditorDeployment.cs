using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using HarmonyLib;

namespace WTT.Campaigns.Client.Authoring;

internal static class EditorDeployment
{
    internal static void Enable()
    {
        var routine = AccessTools.DeclaredMethod(typeof(LocalGame), nameof(LocalGame.vmethod_2));
        var machine =
            routine.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("Missing native local deployment coroutine.");
        new Harmony("com.wtt.campaigns.editor.deployment").Patch(
            AccessTools.DeclaredMethod(machine, "MoveNext"),
            transpiler: new HarmonyMethod(typeof(EditorDeployment), nameof(Transpiler))
        );
    }

    // Preserve the native audio, UI initialization and coroutine completion.
    // Only the editor's screen transition and pre-raid delay change.
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var delay = 0;
        var screen = 0;
        var show = typeof(MatchmakerFinalCountdown.FinalCountdownScreenController).GetMethod("ShowScreen", new[] { typeof(EScreenState) });
        foreach (var instruction in instructions)
        {
            if (
                instruction.opcode == OpCodes.Ldfld
                && instruction.operand is FieldInfo field
                && field.DeclaringType == typeof(GlobalConfiguration)
                && field.Name == nameof(GlobalConfiguration.TimeBeforeDeployLocal)
            )
            {
                delay++;
                yield return instruction;
                yield return new CodeInstruction(
                    OpCodes.Call,
                    typeof(EditorDeployment).GetMethod(nameof(Delay), BindingFlags.NonPublic | BindingFlags.Static)
                );
                continue;
            }
            if (
                instruction.operand is MethodInfo method
                && show != null
                && method.Module == show.Module
                && method.MetadataToken == show.MetadataToken
                && method.DeclaringType == show.DeclaringType
            )
            {
                screen++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(EditorDeployment).GetMethod(nameof(Show), BindingFlags.NonPublic | BindingFlags.Static);
            }
            yield return instruction;
        }
        if (delay != 1 || screen != 1)
            throw new InvalidOperationException(
                $"Expected one native deployment delay and screen transition; found {delay} delays and {screen} transitions."
            );
    }

    private static int Delay(int seconds) => EditorMode.LoadingMap ? 0 : seconds;

    private static void Show(MatchmakerFinalCountdown.FinalCountdownScreenController controller, EScreenState state)
    {
        if (!EditorMode.LoadingMap)
        {
            controller.ShowScreen(state);
            return;
        }
        // The countdown normally closes the loading screen through navigation.
        // Keep that cleanup when skipping it, before MenuUI is destroyed.
        var current = EftScreenManager.Instance.CurrentScreenController;
        if (current is MatchmakerTimeHasCome.TimeHasComeScreenController loading)
            loading.CloseScreen();
    }
}
