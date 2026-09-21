using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>
/// Authoring metadata for a playable mission. The referenced map layout owns the
/// start, ordered checkpoints, exit, scene edits, loot and encounters.
/// </summary>
public sealed class MissionDefinition : ExtensibleJsonModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New mission";
    public string Briefing { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string CompletionConditionId { get; set; } = "";
    public bool CheckpointRetries { get; set; }
    public MissionEnvironmentSettings? Environment { get; set; }

    public bool ShouldSerializeEnvironment() => Environment != null;

    public List<MissionEventRule> Events { get; set; } = new();
    public List<MissionObjective> Objectives { get; set; } = new();
    public List<MissionRequirement> Requirements { get; set; } = new();
}
