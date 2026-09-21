using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Communications;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Authoring.Editor;

namespace WTT.Campaigns.Client.Patches.Session;

// Change only the two raid transition call sites. Native shutdown, backend changes
// and notification reconnects retain full ownership of the underlying connection.
internal sealed class AuthoringRaidNotifications(string methodName) : ModulePatch("WTT.Campaigns.Notifications." + methodName)
{
    protected override MethodBase GetTargetMethod()
    {
        var method = AccessTools.Method(typeof(TarkovApplication), methodName);
        var stateMachine =
            method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("Missing native raid state machine: " + methodName);
        return AccessTools.Method(stateMachine, "MoveNext");
    }

    [PatchTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matched = 0;
        foreach (var instruction in instructions)
        {
            if (
                instruction.operand is MethodInfo method
                && method.DeclaringType == typeof(NotificationManager)
                && method.GetParameters().Length == 0
                && method.Name is nameof(NotificationManager.Deactivate) or nameof(NotificationManager.Activate)
            )
            {
                matched++;
                // Both replacements take the existing manager from the stack.
                // Mutate the instruction so its branch labels/exception blocks survive.
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(AuthoringRaidNotifications).GetMethod(
                    method.Name == nameof(NotificationManager.Deactivate) ? nameof(EnterRaid) : nameof(LeaveRaid),
                    BindingFlags.NonPublic | BindingFlags.Static
                );
            }
            yield return instruction;
        }
        if (matched != 1)
            throw new InvalidOperationException("Expected exactly one native raid notification transition.");
    }

    private static void EnterRaid(NotificationManager manager)
    {
        if (!RaidEditor.KeepNotificationConnection)
            manager.Deactivate();
    }

    private static void LeaveRaid(NotificationManager manager)
    {
        // Authoring raids kept this manager active; native Activate throws if repeated.
        if (!manager.Activated)
            manager.Activate();
    }
}
