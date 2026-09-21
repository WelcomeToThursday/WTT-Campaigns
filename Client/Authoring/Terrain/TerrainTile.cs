using System.Security.Cryptography;
using System.Text;
using GPUInstancer;
using JBooth.MicroSplat;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.TerrainEditing;

internal sealed class TerrainTile
{
    internal UnityEngine.Terrain Surface = null!;
    internal MicroSplatTerrain? Splat;
    internal TerrainBallistic? Ballistic;
    internal GPUInstancerDetailManager[] Managers = Array.Empty<GPUInstancerDetailManager>();
    internal MapTerrainTarget Target = new();
    internal bool Custom;
    internal int Width,
        Height;
    internal Texture2D[] Controls = Array.Empty<Texture2D>();
    internal string[] Textures = Array.Empty<string>(),
        Grass = Array.Empty<string>();
    internal string TextureError = "",
        GrassError = "";

    internal static TerrainTile Inspect(UnityEngine.Terrain terrain)
    {
        if (!terrain || !terrain.terrainData || !terrain.gameObject.scene.IsValid() || !terrain.gameObject.scene.isLoaded)
            throw new InvalidOperationException("Select a loaded terrain tile.");
        if (
            Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > .01f
            || (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f
        )
            throw new InvalidOperationException("Rotated or scaled terrain is not supported by the terrain brush.");
        var tile = new TerrainTile
        {
            Surface = terrain,
            Splat = terrain.GetComponent<MicroSplatTerrain>(),
            Ballistic = terrain.GetComponent<TerrainBallistic>(),
        };
        var data = terrain.terrainData;
        tile.Custom = tile.Splat && tile.Splat!.keywordSO && tile.Splat.keywordSO.IsKeywordEnabled("_CUSTOMSPLATTEXTURES");
        tile.Controls = tile.Custom ? CustomControls(tile.Splat!) : data.alphamapTextures;
        tile.Width = tile.Controls.Length > 0 && tile.Controls[0] ? tile.Controls[0].width : data.alphamapWidth;
        tile.Height = tile.Controls.Length > 0 && tile.Controls[0] ? tile.Controls[0].height : data.alphamapHeight;
        var count = tile.Custom ? tile.Controls.Length * 4 : data.alphamapLayers;
        var material = terrain.materialTemplate;
        var diffuse = material && material.HasProperty("_Diffuse") ? material.GetTexture("_Diffuse") as Texture2DArray : null;
        if (diffuse && tile.Custom)
            count = Math.Min(count, diffuse!.depth);
        count = Math.Min(count, 32);
        tile.Textures = new string[count];
        var layers = data.terrainLayers;
        for (var i = 0; i < count; i++)
            tile.Textures[i] = i < layers.Length && layers[i] ? layers[i].name : "Surface " + (i + 1);
        if (data.alphamapLayers > 32 || (long)tile.Width * tile.Height * Math.Max(1, count) > 33554432)
            tile.TextureError = "This tile exceeds the supported texture painting memory budget (32 million channel samples).";
        else if (count == 0 || tile.Controls.Length == 0)
            tile.TextureError = "This tile has no paintable texture channels.";
        else if (
            !tile.Ballistic
            || tile.Ballistic!.TerrainData != data
            || tile.Ballistic.TerrainMaterials == null
            || tile.Ballistic.TerrainMaterials.Length < count
        )
            tile.TextureError = "This tile does not expose a native sound/impact mapping for every texture channel.";
        else if (!tile.Splat && material && !material.shader.name.StartsWith("Nature/Terrain/", StringComparison.Ordinal))
            tile.TextureError = "This terrain shader needs a texture painting adapter: " + material.shader.name;
        var proxy = terrain.GetComponent<GPUInstancerTerrainProxy>();
        var managers = new List<GPUInstancerDetailManager>();
        if (proxy)
        {
            if (proxy.detailManager)
                managers.Add(proxy.detailManager);
            if (proxy.detailManagerOptic && proxy.detailManagerOptic != proxy.detailManager)
                managers.Add(proxy.detailManagerOptic);
        }
        tile.Managers = managers.ToArray();
        if (proxy && managers.Count == 0)
            tile.GrassError = "The native grass managers have not loaded.";
        var grassCount = managers.Count > 0 ? managers[0].prototypeList.Count : data.detailPrototypes.Length;
        tile.Grass = new string[grassCount];
        for (var i = 0; i < grassCount; i++)
        {
            if (managers.Count > 0)
                tile.Grass[i] = managers[0].prototypeList[i] ? managers[0].prototypeList[i].name : "Missing grass " + i;
            else
            {
                var p = data.detailPrototypes[i];
                tile.Grass[i] =
                    p.prototype ? p.prototype.name
                    : p.prototypeTexture ? p.prototypeTexture.name
                    : "Grass " + (i + 1);
            }
        }
        foreach (var manager in managers)
        {
            if (manager.terrain != terrain || !manager.isInitialized || manager.prototypeList.Count != grassCount)
                tile.GrassError = "Wait for matching native grass managers to finish loading.";
            for (var i = 0; i < Math.Min(grassCount, manager.prototypeList.Count); i++)
                if (!manager.prototypeList[i] || manager.prototypeList[i].name != tile.Grass[i])
                    tile.GrassError = "Main-camera and optic grass palettes do not match.";
        }
        if (grassCount == 0)
            tile.GrassError = "This tile has no grass prototypes.";
        var shape = new StringBuilder();
        shape.Append(typeof(TerrainBallistic).Module.ModuleVersionId).Append('|');
        shape.Append(data.name).Append('|').Append(data.size.ToString("R")).Append('|').Append(terrain.transform.position.ToString("R"));
        shape
            .Append('|')
            .Append(data.heightmapResolution)
            .Append('|')
            .Append(tile.Width)
            .Append('|')
            .Append(tile.Height)
            .Append('|')
            .Append(data.detailResolution);
        shape.Append('|').Append(material ? material.shader.name : "").Append('|').Append(tile.Custom);
        foreach (var control in tile.Controls)
            shape.Append('|').Append(control ? control.name + ":" + control.width + ":" + control.height : "missing");
        foreach (var layer in layers)
            shape
                .Append('|')
                .Append(
                    layer ? layer.name + ":" + (layer.diffuseTexture ? layer.diffuseTexture.name : "") + ":" + layer.tileSize : "missing"
                );
        foreach (var name in tile.Textures)
            shape.Append('|').Append(name);
        foreach (var name in tile.Grass)
            shape.Append('|').Append(name);
        if (tile.Ballistic)
            foreach (var sound in tile.Ballistic!.TerrainMaterials ?? Array.Empty<BaseBallistic.ESurfaceSound>())
                shape.Append('|').Append(sound);
        using var sha = SHA256.Create();
        tile.Target = new()
        {
            Scene = terrain.gameObject.scene.name,
            Path = MapSceneAdapter.PathOf(terrain.transform),
            Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(shape.ToString()))).Replace("-", ""),
            Width = data.size.x,
            Depth = data.size.z,
            Textures = count,
            Grass = grassCount,
        };
        return tile;
    }

    internal static Texture2D[] CustomControls(MicroSplatTerrain splat)
    {
        var textures = new[]
        {
            splat.customControl0,
            splat.customControl1,
            splat.customControl2,
            splat.customControl3,
            splat.customControl4,
            splat.customControl5,
            splat.customControl6,
            splat.customControl7,
        };
        var count = textures.Length;
        while (count > 0 && !textures[count - 1])
            count--;
        Array.Resize(ref textures, count);
        return textures;
    }

    internal static void SetControls(MicroSplatTerrain splat, Texture2D[] controls)
    {
        Texture2D At(int i) => i < controls.Length ? controls[i] : null!;
        splat.customControl0 = At(0);
        splat.customControl1 = At(1);
        splat.customControl2 = At(2);
        splat.customControl3 = At(3);
        splat.customControl4 = At(4);
        splat.customControl5 = At(5);
        splat.customControl6 = At(6);
        splat.customControl7 = At(7);
    }

    internal Texture? Thumbnail(bool grass, int index)
    {
        if (grass)
        {
            if (Managers.Length > 0 && Managers[0].prototypeList[index] is GPUInstancerDetailPrototype prototype)
            {
                if (prototype.prototypeTexture)
                    return prototype.prototypeTexture;
                var renderer = prototype.prefabObject ? prototype.prefabObject.GetComponentInChildren<Renderer>(true) : null;
                return renderer && renderer!.sharedMaterial ? renderer.sharedMaterial.mainTexture : null;
            }
            var p = Surface.terrainData.detailPrototypes[index];
            return p.prototypeTexture;
        }
        var layers = Surface.terrainData.terrainLayers;
        return index < layers.Length && layers[index] ? layers[index].diffuseTexture : null;
    }
}
