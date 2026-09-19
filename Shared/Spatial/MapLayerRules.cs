namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Composes scenery without importing mission routes or encounter activation.</summary>
public static class MapLayerRules
{
    public static string Key(string campaign, string layout) => campaign + "/" + layout;

    public static bool Enabled(string campaign, MapLayout layout, IReadOnlyDictionary<string, bool>? overrides) =>
        overrides != null && overrides.TryGetValue(Key(campaign, layout.Id), out var enabled) ? enabled : layout.ApplyInNormalRaids;

    public static MapLayout? ForCharacter(
        IEnumerable<Seasons.SeasonDefinition> published,
        string seasonId,
        string location,
        IReadOnlyDictionary<string, bool>? overrides = null
    )
    {
        var layouts = published
            .Where(p => seasonId.Length == 0 || p.Id == seasonId)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .SelectMany(p =>
                p.MapLayouts.Where(l => l.Location == location && Authoring.EditorContentRules.Mode(p, l.Id) == Authoring.EditorContentMode.Level)
                    .Select(l =>
                    {
                        var copy = Seasons.SeasonCompiler.Copy(l);
                        copy.ApplyInNormalRaids = Enabled(p.Id, l, seasonId.Length == 0 ? overrides : null);
                        return copy;
                    })
            );
        return Compose(layouts, location);
    }

    public static List<MapLayerContent> ContentForCharacter(
        IEnumerable<Seasons.SeasonDefinition> published, string seasonId, string location,
        IReadOnlyDictionary<string, bool>? overrides = null)
    {
        var result = new List<MapLayerContent>();
        var zoneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in published.Where(p => seasonId.Length == 0 || p.Id == seasonId).OrderBy(p => p.Id, StringComparer.Ordinal))
        foreach (var layout in package.MapLayouts.Where(l => l.Location == location
            && Authoring.EditorContentRules.Mode(package, l.Id) == Authoring.EditorContentMode.Level
            && Enabled(package.Id, l, seasonId.Length == 0 ? overrides : null)))
        {
            var content = new MapLayerContent
            {
                Source = Key(package.Id, layout.Id),
                Zones = Seasons.SeasonCompiler.Copy(package.Zones.Where(z => z.LayoutId == layout.Id && z.Location == location).ToList()),
                Extract = layout.Exit == null ? null : Seasons.SeasonCompiler.Copy(layout.Exit)
            };
            foreach (var zone in content.Zones)
                if (!zoneIds.Add(zone.Id))
                    throw new InvalidOperationException("Enabled levels contain duplicate zone identity: " + zone.Id);
            result.Add(content);
        }
        return result;
    }

    public static MapLayout? Compose(IEnumerable<MapLayout> layouts, string location)
    {
        var enabled = layouts.Where(l => l.ApplyInNormalRaids && l.Location == location).ToArray();
        if (enabled.Length == 0)
            return null;
        var combined = new MapLayout
        {
            Id = enabled[0].Id,
            Name = "Normal raid map layers",
            Location = location,
            Objects = enabled.SelectMany(l => l.Objects).ToList(),
            Doors = enabled.SelectMany(l => l.Doors).ToList(),
            Barriers = enabled.SelectMany(l => l.Barriers).ToList(),
            Loot = enabled.SelectMany(l => l.Loot).ToList(),
        };
        var errors = MapLayoutRules.Errors(combined);
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Enabled layers on " + location + " (" + string.Join(", ", enabled.Select(l => l.Name)) + "): " + string.Join(" ", errors)
            );
        return combined;
    }

    public static IEnumerable<string> Errors(IEnumerable<MapLayout> layouts)
    {
        var saved = layouts.ToArray();
        foreach (var location in saved.Where(l => l.ApplyInNormalRaids).Select(l => l.Location).Distinct())
        {
            string? error = null;
            try
            {
                Compose(saved, location);
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
            }
            if (error != null)
                yield return error;
        }
    }
}
