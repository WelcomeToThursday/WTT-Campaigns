namespace WTT.Campaigns.Shared.Spatial;

public sealed class HazardSettings
{
    public string Kind { get; set; } = "Minefield";
    public bool PlayShotSound { get; set; } = true;
    public bool SuppressedShots { get; set; }
}

public static class HazardRules
{
    public static readonly string[] Kinds = { "Minefield", "Claymore", "Sniper", "BarbedWire" };

    public static string Label(string kind) =>
        kind switch
        {
            "Sniper" => "Sniper zone",
            "BarbedWire" => "Barbed wire",
            _ => kind,
        };

    public static IEnumerable<string> Errors(SeasonZone zone)
    {
        if (zone.Hazard == null)
            yield break;
        if (!Kinds.Contains(zone.Hazard.Kind))
            yield return "Unknown hazard type: " + zone.Id;
        if (zone.Shape != "Box")
            yield return "Hazards require a box volume: " + zone.Id;
        if (zone.Uses == null || zone.Uses.Count != 0 || !string.IsNullOrEmpty(zone.RequiredQuestId))
            yield return "Hazards require a dedicated volume without quest uses or a quest gate: " + zone.Id;
        if (
            zone.Size?.Finite != true
            || zone.Size.X < .1f
            || zone.Size.Y < .1f
            || zone.Size.Z < .1f
            || zone.Size.X > 500
            || zone.Size.Y > 100
            || zone.Size.Z > 500
        )
            yield return "Hazard dimensions must be 0.1–500 m wide/deep and 0.1–100 m high: " + zone.Id;
    }
}
