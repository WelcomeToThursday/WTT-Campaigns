namespace WTT.Campaigns.Shared.Story;

public sealed class StoryAction
{
    public string Id { get; set; } = "";
    public StoryActionType Type { get; set; }
    public string Target { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string ConditionId { get; set; } = "";
    public int Value { get; set; }
    public double StandingChange { get; set; }

    public bool ShouldSerializeStandingChange() => Type == StoryActionType.TraderStanding;

    public StoryVariableScope Scope { get; set; }
}
