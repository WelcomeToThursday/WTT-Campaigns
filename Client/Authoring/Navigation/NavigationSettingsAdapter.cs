using UnityEngine.AI;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal static class NavigationSettingsAdapter
{
    internal static MapNavigationSettings Capture(NavMeshBuildSettings native) =>
        new()
        {
            Radius = native.agentRadius,
            Height = native.agentHeight,
            Slope = native.agentSlope,
            Step = native.agentClimb,
            VoxelSize = native.overrideVoxelSize ? native.voxelSize : native.agentRadius / 3,
            TileSize = native.tileSize,
        };

    internal static NavMeshBuildSettings Apply(NavMeshBuildSettings native, MapNavigationRecipe? recipe)
    {
        var errors = MapNavigationRules.Errors(recipe);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", errors));
        if (recipe?.Settings is not { } settings)
            return native;
        native.agentRadius = settings.Radius;
        native.agentHeight = settings.Height;
        native.agentSlope = settings.Slope;
        native.agentClimb = settings.Step;
        native.overrideVoxelSize = true;
        native.voxelSize = settings.VoxelSize;
        native.overrideTileSize = true;
        native.tileSize = settings.TileSize;
        return native;
    }
}
