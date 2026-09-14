using UnityEngine;
using UnityEngine.UI;

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

    internal static void Prepare(GameObject root)
    {
        var controls = new Dictionary<string, Transform>();
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (!controls.ContainsKey(child.name))
                controls.Add(child.name, child);

        if (
            controls.TryGetValue("CategoryRail", out var rail)
            && !controls.ContainsKey("AI")
            && controls.TryGetValue("Zones", out var zone)
        )
        {
            var button = UnityEngine.Object.Instantiate(zone.gameObject, rail, false);
            button.name = "AI";
            button.GetComponentInChildren<Text>(true).text = "AI";
            var rect = (RectTransform)button.transform;
            // RaidEditorWindows lays out every category, including AI, after
            // initialization. Do not pin this button to a fixed resolution or
            // overlap the route/capture categories on older bundles.
            rect.anchoredPosition = Vector2.zero;
            rect.gameObject.SetActive(true);
        }

        if (!controls.TryGetValue("CreationTools", out var actions) || !controls.TryGetValue("AddBox", out var template))
            throw new InvalidOperationException("The raid editor creation toolbar is missing its button template.");

        Add(actions, template, "AiEncounter", " + Encounter ");
        Add(actions, template, "AiWave", " + Wave ");
        Add(actions, template, "AiRoster", " + Roster ");
        Add(actions, template, "AiSpawn", " + Spawn ");
        Add(actions, template, "AiPatrol", " + Patrol ");
        Add(actions, template, "AiWaypoint", " + Waypoint ");
        Add(actions, template, "AiObserve", "Observe");
        Add(actions, template, "AiPlaytest", "Playtest");
        Add(actions, template, "AiReset", "Reset AI preview");
        Add(actions, template, "AiSimulate", "Simulate event");

        if (
            !controls.TryGetValue("RecordInspector", out var inspector)
            || !controls.TryGetValue("NameGroup", out var field)
            || !controls.TryGetValue("EventKindGroup", out var actionTemplate)
        )
            throw new InvalidOperationException("The raid editor inspector is missing its AI control templates.");

        AddField(inspector, field, "AiTriggerEventId", "Event id");
        AddField(inspector, field, "AiTriggerZoneId", "Trigger zone");
        AddField(inspector, field, "AiWaveDelaySeconds", "Wave delay");
        AddField(inspector, field, "AiRosterCount", "Roster count");
        AddField(inspector, field, "AiRosterSquadId", "Squad");
        AddField(inspector, field, "AiRosterSpawnPoints", "Spawn point ids");
        AddField(inspector, field, "AiRosterPatrolRoute", "Patrol route id");
        AddField(inspector, field, "AiWaypointWaitSeconds", "Waypoint wait");
        AddAction(inspector, actionTemplate, "AiTriggerGroup", "AiTrigger", "Trigger");
        AddAction(inspector, actionTemplate, "AiWaveWaitPreviousGroup", "AiWaveWaitPrevious", "Wave wait mode");
        AddAction(inspector, actionTemplate, "AiRosterRoleGroup", "AiRosterRole", "Roster role");
        AddAction(inspector, actionTemplate, "AiRosterDifficultyGroup", "AiRosterDifficulty", "Roster difficulty");
        AddAction(inspector, actionTemplate, "AiRosterSpawnNextGroup", "AiRosterSpawnNext", "Next spawn point");
        AddAction(inspector, actionTemplate, "AiRosterPatrolNextGroup", "AiRosterPatrolNext", "Next patrol route");
        AddAction(inspector, actionTemplate, "AiPaceGroup", "AiPace", "Patrol pace");
        AddAction(inspector, actionTemplate, "AiCompletionGroup", "AiCompletion", "Patrol completion");
    }

    private static void Add(Transform parent, Transform template, string name, string caption)
    {
        if (parent.Find(name) is { } existing)
        {
            existing.gameObject.SetActive(false);
            return;
        }
        var button = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        button.name = name;
        button.GetComponentInChildren<Text>(true).text = caption;
        // AI controls are presented by RefreshAiWorkspace. Keep them out of
        // other tabs during the first layout pass and while the editor opens.
        button.SetActive(false);
    }

    private static void AddField(Transform parent, Transform template, string name, string caption)
    {
        if (parent.Find(name + "Group") is { } existing)
        {
            existing.gameObject.SetActive(false);
            return;
        }
        var group = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        group.name = name + "Group";
        var input = group.GetComponentInChildren<InputField>(true);
        if (input != null)
            input.name = name;
        var label = group.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.name = name + "Label";
            label.text = caption.ToUpperInvariant();
        }
        InsertBeforeDetails(parent, group.transform);
        group.SetActive(false);
    }

    private static void AddAction(Transform parent, Transform template, string groupName, string name, string caption)
    {
        if (parent.Find(groupName) is { } existing)
        {
            existing.gameObject.SetActive(false);
            return;
        }
        var group = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        group.name = groupName;
        var button = group.GetComponentInChildren<Button>(true);
        if (button == null)
            throw new InvalidOperationException("The raid editor action template has no button.");
        button.name = name;
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
            label.text = caption;
        InsertBeforeDetails(parent, group.transform);
        group.SetActive(false);
    }

    private static void InsertBeforeDetails(Transform parent, Transform child)
    {
        var details = parent.Find("DetailsGroup");
        if (details != null)
            child.SetSiblingIndex(details.GetSiblingIndex());
    }
}
