using System.Collections.Generic;

namespace SeasonalPerks.Shared.Hub;

public sealed class HubDocumentStack
{
    public string Template { get; set; } = "";
    public List<string> Units { get; set; } = new();
}
