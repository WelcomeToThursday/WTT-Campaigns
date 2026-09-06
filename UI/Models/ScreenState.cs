using System;

namespace SeasonalPerks.UI.Models;

public sealed class ScreenState
{
    public PerkEntry[] Perks = Array.Empty<PerkEntry>();
    public CharacterEntry[] Characters = Array.Empty<CharacterEntry>();
    public string[] Selected = Array.Empty<string>();
    public string ActiveMode = "normal";
    public string SelectedCharacterId = "";
    public string SeasonId = "";
    public SeasonEntry[] Seasons = Array.Empty<SeasonEntry>();
    public int StartingPoints;
    public bool EnforceBudget = true;
    public bool AllowEdits = true;
    public bool CanOpenEditor = true;
    public bool IsScav;
}
