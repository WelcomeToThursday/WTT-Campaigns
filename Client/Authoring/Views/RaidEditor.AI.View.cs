namespace WTT.Campaigns.Client.Authoring.Views;

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
        "AiNavigation",
    };

    internal static readonly string[] InspectorGroups =
    {
        "AiTriggerSection",
        "AiWaveSection",
        "AiRosterSection",
        "AiAssignmentSection",
        "AiPatrolSection",
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
