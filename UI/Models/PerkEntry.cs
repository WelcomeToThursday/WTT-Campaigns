using System;

namespace SeasonalPerks.UI.Models;

public sealed class PerkEntry
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public int Points;
    public bool Common;
    public bool Enabled;
    public string Unavailable = "";
    public string[] Conflicts = Array.Empty<string>();
}
