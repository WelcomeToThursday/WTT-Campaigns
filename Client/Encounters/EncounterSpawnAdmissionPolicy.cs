namespace WTT.Campaigns.Client.Encounters;

internal enum EncounterSpawnTaskDecision
{
    PassThrough,
    CompleteNoOp,
    Fault,
}

/// <summary>
/// Pure decision logic for async native spawn prefixes.  Keeping the scheduler distinction
/// independent of EFT types makes the fail-closed branches testable without loading Unity.
/// </summary>
internal static class EncounterSpawnAdmissionPolicy
{
    internal static EncounterSpawnTaskDecision DecideTask(
        bool editorActive,
        bool argumentsMatchCurrentScope,
        bool hasExplicitAdmissionArguments,
        string? declaringTypeName
    )
    {
        if (!editorActive || argumentsMatchCurrentScope)
        {
            return EncounterSpawnTaskDecision.PassThrough;
        }

        return IsAmbientScheduler(declaringTypeName) && !hasExplicitAdmissionArguments
            ? EncounterSpawnTaskDecision.CompleteNoOp
            : EncounterSpawnTaskDecision.Fault;
    }

    internal static bool IsAmbientScheduler(string? declaringTypeName)
    {
        return string.Equals(declaringTypeName, "EFT.BotSpawner", StringComparison.Ordinal)
            || string.Equals(declaringTypeName, "EFT.BotsController", StringComparison.Ordinal);
    }
}
