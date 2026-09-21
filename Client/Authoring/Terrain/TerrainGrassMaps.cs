using GPUInstancer;
using HarmonyLib;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.TerrainEditing;

internal static class TerrainGrassMaps
{
    private static readonly System.Reflection.FieldInfo Partition = AccessTools.Field(typeof(GPUInstancerManager), "spData");

    // EFT's GetDetailLayer assumes TerrainData.detailResolution, but its cached grass
    // prototypes can use smaller maps. Read each prototype's actual spatial cells.
    internal static List<int[,]> Capture(GPUInstancerDetailManager manager)
    {
        var partition =
            Partition.GetValue(manager) as GPUInstancerSpatialPartitioningData<GPUInstancerCell>
            ?? throw new InvalidOperationException("Native grass spatial data is unavailable.");
        var rows = partition.cellRowAndCollumnCountPerTerrain;
        if (
            rows is < 1 or > 1024
            || !partition.GetCell(GPUInstancerCell.CalculateHash(0, 0, 0), out var first)
            || first is not GPUInstancerDetailCell firstCell
            || firstCell.detailMapData == null
        )
            throw new InvalidOperationException("Native grass cells have not finished loading.");
        var maps = new List<int[,]>();
        long samples = 0;
        for (var layer = 0; layer < manager.prototypeList.Count; layer++)
        {
            if (layer >= firstCell.detailMapData.Count || firstCell.detailMapData[layer] == null)
                throw new InvalidOperationException("Native grass prototype density is missing.");
            var length = firstCell.detailMapData[layer].Length;
            var side = (int)Math.Sqrt(length);
            var resolution = checked(side * rows);
            samples += (long)resolution * resolution;
            if (side == 0 || side * side != length || samples > 16777216)
                throw new InvalidOperationException("Native grass density exceeds the supported cell shape or memory budget.");
            if (manager.prototypeList[layer] is not GPUInstancerDetailPrototype prototype || prototype.detailResolution != resolution)
                throw new InvalidOperationException("Native grass cell and prototype resolutions do not match.");
            var map = new int[resolution, resolution];
            for (var z = 0; z < rows; z++)
            for (var x = 0; x < rows; x++)
            {
                if (
                    !partition.GetCell(GPUInstancerCell.CalculateHash(x, 0, z), out var cell)
                    || cell is not GPUInstancerDetailCell detail
                    || detail.detailMapData == null
                    || layer >= detail.detailMapData.Count
                )
                    throw new InvalidOperationException("A native grass density cell is missing; painting was cancelled.");
                MapTerrainPainting.CopyDensityCell(detail.detailMapData[layer], side, x, z, map);
            }
            maps.Add(map);
        }
        return maps;
    }
}
