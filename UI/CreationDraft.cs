using System;

namespace SeasonalPerks.UI;

public sealed class CreationDraft
{
    public string Nickname = "Seasonal";
    public string Side = "";
    public string HeadId = "";
    public string VoiceId = "";
    public bool Appearance;
}

public interface ICreationIdentity : IDisposable
{
    void Back();
}
