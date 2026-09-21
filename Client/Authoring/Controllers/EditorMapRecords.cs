using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Controllers;

// Pure record operations. The map controller applies them inside Session.Edit.
internal static class EditorMapRecords
{
    internal static string NewId() => Guid.NewGuid().ToString("N")[..24];

    internal static MapLayout DuplicateLayout(MapLayout source)
    {
        var copy = SeasonCompiler.Copy(source);
        var ids = MapLayoutRules.OwnedIds(copy).AsValueEnumerable().ToDictionary(id => id, _ => NewId());
        ModelGraph.Rewrite(copy, value => ids.TryGetValue(value, out var fresh) ? fresh : value);
        if (copy.PlayerRouteSpline != null)
            foreach (var knot in copy.PlayerRouteSpline.Knots)
                knot.Id = NewId();
        foreach (var route in copy.PatrolRoutes)
            if (route.Spline != null)
                foreach (var knot in route.Spline.Knots)
                    knot.Id = NewId();
        copy.Name += " copy";
        return copy;
    }

    internal static List<NativeItem> FreshItems(List<NativeItem> source)
    {
        var copy = SeasonCompiler.Copy(source);
        var ids = copy.AsValueEnumerable().ToDictionary(i => i.Id, _ => NewId());
        foreach (var item in copy)
        {
            var old = item.Id;
            item.Id = ids[old];
            if (item.ParentId != null && ids.TryGetValue(item.ParentId, out var parent))
                item.ParentId = parent;
        }
        return copy;
    }

    internal static MapVolume DuplicateVolume(MapLayout layout, MapVolume source)
    {
        var copy = SeasonCompiler.Copy(source);
        copy.Id = NewId();
        if (layout.Barriers.Contains(source))
            layout.Barriers.Add(copy);
        else
            PlayerRoute.Insert(layout, source.Id, copy);
        return copy;
    }

    internal static void Remove(MapLayout layout, string id)
    {
        layout.Objects.RemoveAll(p => p.Id == id);
        layout.Loot.RemoveAll(p => p.Id == id);
        layout.Doors.RemoveAll(p => p.Id == id);
        layout.Barriers.RemoveAll(p => p.Id == id);
        layout.Checkpoints.RemoveAll(p => p.Id == id);
        if (layout.Start?.Id == id)
            layout.Start = null;
        if (layout.Exit?.Id == id)
            layout.Exit = null;
    }

    internal static void ReorderCheckpoint(MapLayout layout, string id, int direction)
    {
        var index = layout.Checkpoints.FindIndex(p => p.Id == id);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= layout.Checkpoints.Count)
            return;
        var point = layout.Checkpoints[index];
        layout.Checkpoints.RemoveAt(index);
        layout.Checkpoints.Insert(next, point);
    }
}
