using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SeasonalPerks.Server.Profiles;

public sealed class CharacterVisual
{
    public CharacterVisualInfo Info { get; set; } = new();
    public Customization? Customization { get; set; }
    public CharacterEquipment Equipment { get; set; } = new();
}
