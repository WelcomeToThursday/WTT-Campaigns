using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    /// <summary>
    /// Returns zones for the connected map. Editor mode narrows the list to the selected layout plus Shared zones;
    /// ordinary raid authoring keeps its existing global zone view.
    /// </summary>
    internal IEnumerable<SeasonZone> FilterZonesForLayout(string layoutId, bool includeShared = true)
    {
        var zones = _session?.Definition?.Zones;
        if (zones == null)
        {
            return Array.Empty<SeasonZone>();
        }

        var mapZones = zones.AsValueEnumerable().Where(z => z != null && z.Location == _session!.Location).ToArray();
        return EditorMode.Ready ? ZoneLayoutRules.ForEditor(mapZones, layoutId, includeShared) : mapZones;
    }

    /// <summary>Assigns a zone to a concrete layout, or clears LayoutId to make it Shared.</summary>
    internal bool SetZoneLayout(string zoneId, string? layoutId, out string error)
    {
        error = "";
        layoutId ??= "";
        if (_session?.Definition == null)
        {
            error = "Connect a draft in the web editor first.";
            return false;
        }

        if (!EditorMode.Ready && layoutId.Length > 0)
        {
            error = "Layout-owned zones can only be edited in Editor mode.";
            return false;
        }

        try
        {
            _session.Edit(s =>
            {
                var zone = s.Zones.AsValueEnumerable().FirstOrDefault(z => z != null && z.Id == zoneId);
                if (zone == null)
                {
                    throw new InvalidOperationException("The selected zone was deleted.");
                }

                var ownershipError = ZoneLayoutRules.OwnershipError(s, layoutId, zone.Location);
                if (ownershipError.Length > 0)
                {
                    throw new InvalidOperationException(ownershipError);
                }

                zone.LayoutId = layoutId;
            });
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    internal bool SetSelectedZoneLayout(string? layoutId, out string error)
    {
        return SetZoneLayout(_selected, layoutId, out error);
    }

    /// <summary>Copies the source layout's own zones into an existing destination layout in one session edit.</summary>
    internal bool TryCopyLayoutZones(string sourceLayoutId, string destinationLayoutId, out string error)
    {
        error = "";
        if (_session?.Definition == null)
        {
            error = "Connect a draft in the web editor first.";
            return false;
        }

        if (!EditorMode.Ready)
        {
            error = "Layout-owned zones can only be copied in Editor mode.";
            return false;
        }

        try
        {
            _session.Edit(s => s.Zones.AddRange(ZoneLayoutRules.CopyOwnedZones(s, sourceLayoutId, destinationLayoutId)));
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>Deletes a layout and its own zones only when no quest or story record references those zones.</summary>
    internal bool TryDeleteLayoutZones(string layoutId, out string error)
    {
        error = "";
        if (_session?.Definition == null)
        {
            error = "Connect a draft in the web editor first.";
            return false;
        }

        if (!EditorMode.Ready)
        {
            error = "Layouts can only be deleted in Editor mode.";
            return false;
        }

        try
        {
            _session.Edit(s =>
            {
                if (!ZoneLayoutRules.TryDeleteLayout(s, layoutId, out var failure))
                {
                    throw new InvalidOperationException(failure);
                }
            });
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }
}
