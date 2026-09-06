using System;
using System.Collections.Generic;

namespace SeasonalPerks.UI.Models;

[Serializable]
public sealed class HubAction
{
    public string Action = "claim";
    public long ExpectedRevision;
    public string RewardId = "";
    public bool UseClassified;
    public string DocumentId = "";
    public bool Crate;
    public Dictionary<string, int> Sources = new Dictionary<string, int>();
}
