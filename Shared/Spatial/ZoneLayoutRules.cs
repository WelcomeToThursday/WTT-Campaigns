using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Shared-zone and layout-zone ownership rules used by authoring and runtime boundaries.</summary>
public static class ZoneLayoutRules
{
    /// <summary>Returns true when the zone is available to every layout on its map.</summary>
    public static bool IsShared(SeasonZone zone)
    {
        return zone != null && string.IsNullOrEmpty(zone.LayoutId);
    }

    /// <summary>Returns the zones visible while editing the selected layout, including Shared zones when requested.</summary>
    public static IEnumerable<SeasonZone> ForEditor(IEnumerable<SeasonZone>? zones, string? layoutId, bool includeShared = true)
    {
        if (zones == null)
        {
            return Enumerable.Empty<SeasonZone>();
        }

        layoutId ??= "";
        return zones.Where(z =>
            z != null && (string.Equals(z.LayoutId ?? "", layoutId, StringComparison.Ordinal) || includeShared && IsShared(z))
        );
    }

    /// <summary>Returns only zones owned by a concrete layout. Empty ownership is Shared, never a layout owner.</summary>
    public static IEnumerable<SeasonZone> OwnedByLayout(IEnumerable<SeasonZone>? zones, string? layoutId)
    {
        if (zones == null || string.IsNullOrEmpty(layoutId))
        {
            return Enumerable.Empty<SeasonZone>();
        }

        return zones.Where(z => z != null && string.Equals(z.LayoutId, layoutId, StringComparison.Ordinal));
    }

    /// <summary>Returns the validation error for assigning a zone on a map to the requested layout.</summary>
    public static string OwnershipError(SeasonDefinition season, string? layoutId, string location)
    {
        if (string.IsNullOrEmpty(layoutId))
        {
            return "";
        }

        var layout = season.MapLayouts.FirstOrDefault(l => l != null && l.Id == layoutId);
        if (layout == null)
        {
            return "The selected layout does not exist.";
        }

        return layout.Location == location ? "" : "The selected layout belongs to a different map.";
    }

    /// <summary>Validates every non-Shared zone owner in a season.</summary>
    public static List<string> Errors(SeasonDefinition season)
    {
        var errors = new List<string>();
        foreach (var zone in season.Zones ?? new())
        {
            if (zone == null || string.IsNullOrEmpty(zone.LayoutId))
            {
                continue;
            }

            var error = OwnershipError(season, zone.LayoutId, zone.Location);
            if (error.Length > 0)
            {
                errors.Add(error + " Zone: " + zone.Id);
            }
        }

        return errors;
    }

    /// <summary>
    /// Copies the zones owned by one existing layout to another existing layout on the same map.
    /// Returned records are detached from the season so callers can add them in their own transaction.
    /// </summary>
    public static List<SeasonZone> CopyOwnedZones(SeasonDefinition season, string sourceLayoutId, string destinationLayoutId)
    {
        if (string.IsNullOrEmpty(sourceLayoutId) || string.IsNullOrEmpty(destinationLayoutId))
        {
            throw new InvalidOperationException("Layout zone copies require source and destination layouts.");
        }

        if (sourceLayoutId == destinationLayoutId)
        {
            throw new InvalidOperationException("Choose a different destination layout for the zone copy.");
        }

        var source = season.MapLayouts.FirstOrDefault(l => l != null && l.Id == sourceLayoutId);
        var destination = season.MapLayouts.FirstOrDefault(l => l != null && l.Id == destinationLayoutId);
        if (source == null || destination == null)
        {
            throw new InvalidOperationException("Both layouts must exist before copying their zones.");
        }

        if (source.Location != destination.Location)
        {
            throw new InvalidOperationException("Layout zone copies require layouts on the same map.");
        }

        var zones = OwnedByLayout(season.Zones, sourceLayoutId).ToArray();
        if (OwnedByLayout(season.Zones, destinationLayoutId).Any())
        {
            throw new InvalidOperationException("The destination layout already owns zones.");
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in season.Zones ?? new())
        {
            if (zone != null)
            {
                used.Add(zone.Id);
            }
        }
        foreach (var capture in season.Captures ?? new())
        {
            if (capture != null)
            {
                used.Add(capture.Id);
            }
        }
        foreach (var layout in season.MapLayouts ?? new())
        {
            if (layout != null)
            {
                foreach (var id in MapLayoutRules.OwnedIds(layout))
                {
                    used.Add(id);
                }
            }
        }

        var copies = new List<SeasonZone>(zones.Length);
        foreach (var zone in zones)
        {
            var copy = SeasonCompiler.Copy(zone);
            copy.Id = NewId(used);
            copy.LayoutId = destinationLayoutId;
            copies.Add(copy);
        }

        return copies;
    }

    /// <summary>
    /// Removes a layout and its owned zones after checking all native condition and story references.
    /// A failed check leaves the supplied definition unchanged.
    /// </summary>
    public static bool TryDeleteLayout(SeasonDefinition season, string layoutId, out string error)
    {
        return TryDelete(season, layoutId, deleteLayout: true, out error);
    }

    /// <summary>Removes only zones owned by a layout after the same dangling-reference check.</summary>
    public static bool TryDeleteOwnedZones(SeasonDefinition season, string layoutId, out string error)
    {
        return TryDelete(season, layoutId, deleteLayout: false, out error);
    }

    private static bool TryDelete(SeasonDefinition season, string layoutId, bool deleteLayout, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(layoutId))
        {
            error = "Shared zones do not belong to a layout.";
            return false;
        }

        var layout = season.MapLayouts.FirstOrDefault(l => l != null && l.Id == layoutId);
        if (layout == null)
        {
            error = "The selected layout does not exist.";
            return false;
        }

        if (deleteLayout)
        {
            var missions = season
                .Missions.Where(mission => string.Equals(mission.LayoutId, layoutId, StringComparison.Ordinal))
                .Select(mission => mission.Name + " · Mission " + mission.Id)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (missions.Length > 0)
            {
                error = "Reassign mission references before deleting layout " + layout.Name + ": " + string.Join(", ", missions);
                return false;
            }
        }

        var zones = OwnedByLayout(season.Zones, layoutId).ToArray();
        var uses = zones
            .SelectMany(z => SpatialRules.Uses(season, z.Id).Select(use => z.Name + " · " + use))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (uses.Length > 0)
        {
            error = "Reassign references before deleting layout " + layout.Name + ": " + string.Join(", ", uses);
            return false;
        }

        season.Zones.RemoveAll(z => z != null && string.Equals(z.LayoutId, layoutId, StringComparison.Ordinal));
        if (deleteLayout)
        {
            season.MapLayouts.RemoveAll(l => l != null && l.Id == layoutId);
        }

        return true;
    }

    private static string NewId(ISet<string> used)
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 24);
        } while (!used.Add(id));

        return id;
    }
}
