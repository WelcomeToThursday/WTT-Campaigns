using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Patches.Session;

public abstract class SeasonProfilePathPatch(Type type, string method) : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        var target = AccessTools.Method(type, method);
        var stateMachine = target.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return stateMachine == null ? target : AccessTools.Method(stateMachine, "MoveNext");
    }

    protected static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var combine = AccessTools.Method(typeof(Path), nameof(Path.Combine), [typeof(string), typeof(string)]);
        var getFiles = AccessTools.Method(typeof(FileUtil), nameof(FileUtil.GetFiles), [typeof(string), typeof(bool), typeof(string)]);
        var changed = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(combine))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(SeasonProfileStorage), nameof(SeasonProfileStorage.Combine));
                changed = true;
            }
            if (instruction.Calls(getFiles))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(SeasonProfileStorage), nameof(SeasonProfileStorage.BackupFiles));
            }
            yield return instruction;
        }
        if (!changed)
        {
            throw new InvalidOperationException("SPT profile storage layout changed; refusing an incomplete storage patch.");
        }
    }
}

[Injectable]
public sealed class SeasonProfileSavePathPatch() : SeasonProfilePathPatch(typeof(SaveServer), nameof(SaveServer.SaveProfileAsync))
{
    [PatchTranspiler, UsedImplicitly]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Rewrite(instructions);
    }
}

[Injectable]
public sealed class SeasonProfileLoadPathPatch() : SeasonProfilePathPatch(typeof(SaveServer), nameof(SaveServer.LoadProfileAsync))
{
    [PatchTranspiler, UsedImplicitly]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Rewrite(instructions);
    }
}

[Injectable]
public sealed class SeasonProfileRemovePathPatch() : SeasonProfilePathPatch(typeof(SaveServer), nameof(SaveServer.RemoveProfile))
{
    [PatchTranspiler, UsedImplicitly]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Rewrite(instructions);
    }
}

[Injectable]
public sealed class SeasonProfileBackupPathPatch() : SeasonProfilePathPatch(typeof(BackupService), nameof(BackupService.InitializeAsync))
{
    [PatchTranspiler, UsedImplicitly]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Rewrite(instructions);
    }
}

[Injectable]
public sealed class SeasonProfileRestorePathPatch() : SeasonProfilePathPatch(typeof(BackupService), nameof(BackupService.RestoreProfile))
{
    [PatchTranspiler, UsedImplicitly]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Rewrite(instructions);
    }
}
