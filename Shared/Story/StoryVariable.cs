namespace WTT.Campaigns.Shared.Story;

public sealed class StoryVariable
{
    public string Id { get; set; } = "";
    public StoryVariableScope Scope { get; set; }
    public int InitialValue { get; set; }
}
