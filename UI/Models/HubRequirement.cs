using System;

namespace SeasonalPerks.UI.Models;

[Serializable]
public sealed class HubRequirement
{
    public string Kind = "";
    public string Target = "";
    public int Required;
    public int Current;
    public bool Met;
    public string UnavailableReason = "";
}
