namespace SeasonalPerks.UI.Models;

public sealed class CharacterEntry
{
    public string Side = "Usec";
    public string Mode = "";
    public string Name = "";
    public int Level;
    public bool Exists;

    // The presentation can bind these when seasonal progression is implemented.
    public string BattlePassRewards = "";
    public string StoryChapters = "";
    public string Kd = "";
    public string Survivals = "";
}
