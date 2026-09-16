using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class PlayerRoute
{
    internal static void Insert(MapLayout layout, string selected, MapVolume checkpoint)
    {
        var after = layout.Checkpoints.FindIndex(p => p.Id == selected);
        layout.Checkpoints.Insert(after < 0 ? layout.Checkpoints.Count : after + 1, checkpoint);
    }

    internal static string Progress(MapLayout layout, int reached) =>
        reached > layout.Checkpoints.Count ? "Route complete"
        : reached == layout.Checkpoints.Count ? "Head to exit · " + layout.Exit?.Name
        : $"Checkpoint {reached + 1} / {layout.Checkpoints.Count} · {layout.Checkpoints[reached].Name}";
}
