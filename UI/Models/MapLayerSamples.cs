namespace WTT.Campaigns.UI.Models;

/// <summary>Presentation-only records; their switches never reach the layer service.</summary>
public static class MapLayerSamples
{
    public static MapLayerEntry[] Create() =>
        new[]
        {
            Entry("customs-yard", "Customs", "TEST · Storage yard scenery", true),
            Entry("customs-road", "Customs", "TEST · Roadside barriers", false),
            Entry("customs-long", "Customs", "TEST · Warehouse loading area — extended layer name alignment check", false),
            Entry("interchange-store", "Interchange", "TEST · Storefront props", true),
            Entry("interchange-parking", "Interchange", "TEST · Parking garage containers", false),
            Entry("woods-camp", "Woods", "TEST · Campsite scenery", true),
            Entry("woods-trail", "Woods", "TEST · Trail barriers", false),
            Entry("woods-loot", "Woods", "TEST · Supply cache placements", true),
        };

    private static MapLayerEntry Entry(string id, string map, string name, bool enabled) =>
        new()
        {
            Key = "ui-sample/" + id,
            Location = map,
            Name = name,
            Campaign = "UI samples · no changes to raids",
            Enabled = enabled,
        };
}
