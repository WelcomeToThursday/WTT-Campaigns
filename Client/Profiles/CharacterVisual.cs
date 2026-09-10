using EFT;

namespace WTT.Campaigns.Client.Profiles;

public sealed class CharacterVisual
{
    public JsonType.PlayerInfo Info { get; set; } = new();
    public Dictionary<EBodyModelPart, MongoID> Customization { get; set; } = new();
    public CharacterEquipment Equipment { get; set; } = new();

    public PlayerVisualRepresentationDescriptor CreateDescriptor()
    {
        var equipment = new InventoryEquipmentDescriptor { _id = Equipment.Id, _items = Equipment.Items };
        // Building the equipment tree requires ItemFactory. Defer it until the preview
        // opens; snapshots also load during backend creation before the factory exists.
        equipment.OnJSONDeserialized(default);
        return new PlayerVisualRepresentationDescriptor
        {
            Info = Info,
            Customization = Customization,
            Equipment = equipment,
        };
    }
}
