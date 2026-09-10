namespace WTT.Campaigns.UI.Models;

public sealed class CharacterEntry
{
    public string Side = "Usec";
    public string Mode = "";
    public string Id = "";
    public string SeasonId = "";
    public string SeasonName = "";
    public bool Available = true;
    public string Name = "";
    public int Level;
    public bool Exists;
    public bool Wiped;

    // The presentation can bind these when seasonal progression is implemented.
    public string BattlePassRewards = "";
    public string StoryChapters = "";
    public string Kd = "";
    public string Survivals = "";
}
