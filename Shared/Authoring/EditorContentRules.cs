using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Authoring;

public enum EditorContentMode
{
    None,
    Mission,
    Level,
}

public static class EditorContentRules
{
    public static List<EditorLayoutChoice> Levels(IEnumerable<(string Id, SeasonDefinition Definition)> drafts) =>
        drafts
            .AsValueEnumerable()
            .SelectMany(d =>
                d.Definition.MapLayouts.AsValueEnumerable()
                    .Where(l => Mode(d.Definition, l.Id) == EditorContentMode.Level)
                    .Select(l => new EditorLayoutChoice
                    {
                        DraftId = d.Id,
                        Id = l.Id,
                        Name = l.Name,
                        Location = l.Location,
                        Mode = EditorContentMode.Level,
                    })
            )
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.DraftId)
            .ThenBy(l => l.Id)
            .ToList();

    public static List<EditorContentMode> AvailableModes(SeasonDefinition definition) =>
        definition.MapLayouts.AsValueEnumerable().Select(l => Mode(definition, l.Id)).Append(Mode(definition)).Distinct().ToList();

    public static bool Includes(EditorDraftChoice draft, EditorContentMode mode) =>
        draft.Modes.Count > 0 ? draft.Modes.Contains(mode) : draft.Mode == mode;

    public static EditorContentMode Mode(SeasonDefinition? definition, string layoutId = "")
    {
        if (definition == null)
            return EditorContentMode.None;
        if (
            definition.MissionPackage != null
            || definition.Missions.AsValueEnumerable().Any(m => m.LayoutId == layoutId)
            || (
                layoutId.Length > 0
                && definition
                    .MissionLinks.AsValueEnumerable()
                    .Any(link => link.Package.MapLayouts.AsValueEnumerable().Any(l => l.Id == layoutId))
            )
        )
            return EditorContentMode.Mission;
        return EditorContentMode.Level;
    }

    public static string Title(EditorContentMode mode) =>
        mode switch
        {
            EditorContentMode.Mission => "Mission Editor",
            EditorContentMode.Level => "Level Editor",
            _ => "Editor",
        };

    public static bool ToolAllowed(EditorContentMode mode, string tool, bool hasStory) =>
        tool == "AI" ? mode == EditorContentMode.Mission : tool != "Bindings" || hasStory;

    public static bool ActionAllowed(EditorContentMode mode, string action) =>
        mode == EditorContentMode.Mission
        || action is not ("MapStart" or "MapCheckpoint" or "AiObserve" or "AiPlaytest" or "TestCheckpoints");

    public static bool QuestEligible(string requiredQuest, string? status) =>
        requiredQuest.Length == 0 || status is "Started" or "AvailableForFinish";

    // Compare protected fields to the saved content, allowing old documents to round-trip unchanged.
    public static void ValidateEdit(SeasonDefinition before, SeasonDefinition after)
    {
        foreach (var layout in after.MapLayouts)
        {
            var prior = before.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == layout.Id);
            if (Mode(before, layout.Id) == EditorContentMode.Mission)
            {
                if (layout.ApplyInNormalRaids && prior?.ApplyInNormalRaids != true)
                    throw new InvalidOperationException("Mission layouts cannot apply in normal raids.");
                continue;
            }
            prior ??= new MapLayout();
            if (!JToken.DeepEquals(Protected(prior), Protected(layout)))
                throw new InvalidOperationException("Authored AI, checkpoints and player starts require Mission Editor.");
        }
        // A layout edit cannot grant itself mission capabilities by changing ownership.
        if (
            !JToken.DeepEquals(
                JArray.FromObject(before.Missions.AsValueEnumerable().Select(m => new { m.Id, m.LayoutId }).ToArray()),
                JArray.FromObject(after.Missions.AsValueEnumerable().Select(m => new { m.Id, m.LayoutId }).ToArray())
            )
        )
            throw new InvalidOperationException("Change mission ownership in Creator before opening the map.");
    }

    private static JObject Protected(MapLayout layout) =>
        JObject.FromObject(
            new
            {
                layout.Start,
                layout.Checkpoints,
                layout.SpawnPoints,
                layout.Encounters,
                layout.PatrolRoutes,
            }
        );
}
