using System;

namespace WTT.Campaigns.UI.Models;

[Serializable]
public sealed class HubDocument
{
    public string Id = "";
    public string Name = "";
    public string Image = "";
    public string UnavailableImage = "";
    public int Count = 0;
}
