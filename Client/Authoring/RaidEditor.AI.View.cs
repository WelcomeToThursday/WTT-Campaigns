namespace WTT.Campaigns.Client.Authoring;

internal static class RaidEditorAiView
{
    internal static readonly string[] CreationControls =
    {
        "AiEncounter",
        "AiWave",
        "AiRoster",
        "AiSpawn",
        "AiPatrol",
        "AiWaypoint",
        "AiObserve",
        "AiPlaytest",
        "AiReset",
        "AiSimulate",
    };

    internal static readonly string[] InspectorGroups =
    {
        "AiTriggerGroup",
        "AiWaveWaitPreviousGroup",
        "AiRosterRoleGroup",
        "AiRosterDifficultyGroup",
        "AiRosterSpawnNextGroup",
        "AiRosterPatrolNextGroup",
        "AiPaceGroup",
        "AiCompletionGroup",
    };

    internal static readonly string[] InspectorFields =
    {
        "AiTriggerEventId",
        "AiTriggerZoneId",
        "AiWaveDelaySeconds",
        "AiRosterCount",
        "AiRosterSquadId",
        "AiRosterSpawnPoints",
        "AiRosterPatrolRoute",
        "AiWaypointWaitSeconds",
    };
}
