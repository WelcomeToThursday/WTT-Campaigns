using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// A camera-scoped GPU surface, captured by the existing native-frame editor viewport.
// No scene objects/colliders or changes to camera targets, rectangles or native geometry.
internal sealed class NavigationSurfaceRenderer : IDisposable
{
    private static readonly List<NavigationSurfaceRenderer> CompositeSurfaces = new();
    private Camera? _compositeCamera;
    private int _compositeFrame = -1;
    private bool _queued;
    private MaterialPropertyBlock? _overlayProperties;
    private Mesh? _surface;
    private Terrain?[] _receivers = Array.Empty<Terrain?>();
    private string _receiverSummary = "";
    private Material? _material;
    private int _projectionPass = -1;
    private bool _materialInitialized,
        _lastDiagnostics,
        _lastStale;
    private int _lastFilter;
    private Color _lastColor;
    private bool _lastProjectOntoGround;
    internal static bool ProjectOntoGround { get; set; } = true;
    internal bool Diagnostics { get; set; }
    internal bool Stale { get; set; }
    internal int IssueFilter { get; set; }
    internal int Triangles { get; private set; }
    internal Color Color { get; set; } = new(1f, .12f, .8f, .38f);

    internal void Capture(NavMeshTriangulation data, float? floor, Vector2[]? issues = null)
    {
        Clear();
        if (data.indices == null || data.indices.Length == 0)
            return;
        // Ground projection must retain a triangle whose physical surface crosses
        // the selected floor even when all of its coarse vertices are below it.
        var indices = data.indices;
        if (indices.Length == 0)
            return;
        try
        {
            if (!_material)
                _material = new Material(EditorToolkitDocument.NavigationShader)
                {
                    name = "CampaignEditor navigation surface",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            _projectionPass = _material!.FindPass("GroundProjection");
            if (_projectionPass < 0)
                throw new InvalidOperationException("Navigation ground projection shader pass is missing.");
            var settings = NavMesh.GetSettingsByID(0);
            if (settings.agentRadius > 0 && settings.agentHeight > 0)
            {
                var voxel = settings.overrideVoxelSize ? settings.voxelSize : settings.agentRadius / 3;
                _material.SetVector(
                    "_MeshContactRange",
                    new Vector4(
                        settings.agentClimb + 2 * voxel + .06f,
                        voxel + .06f,
                        settings.agentClimb,
                        Mathf.Cos((settings.agentSlope + 2) * Mathf.Deg2Rad)
                    )
                );
            }
            _material.SetVector(
                "_FloorBand",
                floor.HasValue ? new Vector4(floor.Value - .6f, floor.Value + .6f, 0, 0) : new Vector4(-100000, 100000, 0, 0)
            );
            _surface = new Mesh
            {
                name = "CampaignEditor navigation surface",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32,
            };
            _surface.vertices = data.vertices;
            CaptureReceivers(data.vertices, indices);
            if (issues != null)
                _surface.uv = issues;
            _surface.RecalculateBounds();
            _surface.UploadMeshData(true);
            Triangles = indices.Length / 3;
        }
        catch
        {
            Clear();
            throw;
        }
    }

    internal void Draw(Camera camera)
    {
        if (_surface && _material && camera)
        {
            if (
                !_materialInitialized
                || _lastColor != Color
                || _lastDiagnostics != Diagnostics
                || _lastStale != Stale
                || _lastFilter != IssueFilter
                || _lastProjectOntoGround != ProjectOntoGround
            )
            {
                _material!.color = Color;
                _material.renderQueue = Diagnostics ? 3020 : 3010;
                _material.SetFloat("_Diagnostic", Diagnostics ? 1 : 0);
                _material.SetFloat("_Stale", Stale ? 1 : 0);
                _material.SetFloat("_IssueFilter", IssueFilter);
                _materialInitialized = true;
                _lastColor = Color;
                _lastDiagnostics = Diagnostics;
                _lastStale = Stale;
                _lastFilter = IssueFilter;
                _lastProjectOntoGround = ProjectOntoGround;
            }
            if (ProjectOntoGround)
            {
                // Submit footprints to the depth projector. The final viewport receives
                // colour at visible scene surfaces, not at these triangle heights.
                _compositeCamera = camera;
                _compositeFrame = Time.frameCount;
                if (!_queued)
                {
                    CompositeSurfaces.Add(this);
                    _queued = true;
                }
                return;
            }
            for (var submesh = 0; submesh < _surface!.subMeshCount; submesh++)
                Graphics.DrawMesh(
                    _surface!,
                    Matrix4x4.Translate(Vector3.up * .04f),
                    _material!,
                    0,
                    camera,
                    submesh,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    null,
                    LightProbeUsage.Off
                );
        }
    }

    private void CaptureReceivers(Vector3[] vertices, int[] indices)
    {
        // Resolve once when the snapshot changes, never by raycasting or reading
        // back scene depth each frame. Each draw uses one native terrain texture.
        var terrains = Terrain.activeTerrains;
        var owners = new int[vertices.Length];
        for (var v = 0; v < vertices.Length; v++)
        {
            owners[v] = -1;
            var best = float.PositiveInfinity;
            for (var t = 0; t < terrains.Length; t++)
            {
                var terrain = terrains[t];
                var data = terrain.terrainData;
                if (!data || terrain.transform.rotation != Quaternion.identity || terrain.transform.lossyScale != Vector3.one)
                    continue;
                var local = vertices[v] - terrain.transform.position;
                if (local.x < 0 || local.z < 0 || local.x > data.size.x || local.z > data.size.z)
                    continue;
                var offset = local.y - data.GetInterpolatedHeight(local.x / data.size.x, local.z / data.size.z);
                // Coarse navigation may cut below a curved hillside. Preserve
                // that ground receiver, without assigning an elevated floor to
                // the terrain underneath it.
                if (offset < -2 || offset > .35f)
                    continue;
                var distance = Mathf.Abs(offset);
                if (distance >= best)
                    continue;
                best = distance;
                owners[v] = t;
            }
        }
        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < indices.Length; i += 3)
        {
            var owner = owners[indices[i]];
            if (owners[indices[i + 1]] != owner || owners[indices[i + 2]] != owner)
                owner = -1;
            if (!groups.TryGetValue(owner, out var group))
                groups[owner] = group = new List<int>();
            group.Add(indices[i]);
            group.Add(indices[i + 1]);
            group.Add(indices[i + 2]);
        }
        _surface!.subMeshCount = groups.Count;
        _receivers = new Terrain?[groups.Count];
        var submesh = 0;
        var descriptions = new List<string>();
        var terrainTriangles = 0;
        foreach (var group in groups)
        {
            _receivers[submesh] = group.Key >= 0 ? terrains[group.Key] : null;
            if (group.Key >= 0)
            {
                var terrain = terrains[group.Key];
                descriptions.Add($"{terrain.name}: pixel error {terrain.heightmapPixelError}");
                terrainTriangles += group.Value.Count / 3;
            }
            else
                descriptions.Add("mesh-floor contact");
            _surface.SetTriangles(group.Value, submesh++);
        }
        var summary = string.Join("; ", descriptions);
        if (_receiverSummary != summary)
        {
            _receiverSummary = summary;
            Plugin.LogInfo(
                $"Navigation overlay receivers: {summary}; terrain triangles={terrainTriangles}; mesh-floor triangles={indices.Length / 3 - terrainTriangles}."
            );
        }
    }

    private void SetReceiver(MaterialPropertyBlock properties, int submesh)
    {
        var terrain = _receivers[submesh];
        properties.SetFloat("_TerrainReceiver", terrain ? 1 : 0);
        if (!terrain)
            return;
        var data = terrain!.terrainData;
        var origin = terrain.transform.position;
        var size = data.size;
        var resolution = data.heightmapResolution;
        properties.SetTexture("_TerrainHeightmap", data.heightmapTexture);
        properties.SetFloat("_TerrainPixelError", terrain.heightmapPixelError);
        properties.SetVector("_TerrainRegion", new Vector4(origin.x, origin.z, 1 / size.x, 1 / size.z));
        properties.SetVector(
            "_TerrainHeight",
            new Vector4(origin.y, size.y * (65535f / 32766f), (resolution - 1f) / resolution, .5f / resolution)
        );
    }

    internal static int RecordOverlay(Camera camera, CommandBuffer commands)
    {
        if (!ProjectOntoGround || !camera)
            return 0;
        var triangles = 0;
        for (var diagnostics = 0; diagnostics < 2; diagnostics++)
            foreach (var surface in CompositeSurfaces)
            {
                if (
                    surface._compositeCamera != camera
                    || surface._compositeFrame != Time.frameCount
                    || !surface._surface
                    || !surface._material
                    || surface.Diagnostics != (diagnostics == 1)
                )
                    continue;
                surface._overlayProperties ??= new MaterialPropertyBlock();
                surface._overlayProperties.SetFloat("_ProjectGround", 1);
                for (var submesh = 0; submesh < surface._surface!.subMeshCount; submesh++)
                {
                    surface.SetReceiver(surface._overlayProperties, submesh);
                    commands.DrawMesh(
                        surface._surface!,
                        Matrix4x4.identity,
                        surface._material!,
                        submesh,
                        surface._projectionPass,
                        surface._overlayProperties
                    );
                }
                triangles += surface.Triangles;
            }
        return triangles;
    }

    internal void Clear()
    {
        CompositeSurfaces.Remove(this);
        _queued = false;
        _compositeCamera = null;
        _compositeFrame = -1;
        if (_surface)
            UnityEngine.Object.Destroy(_surface);
        _surface = null;
        _receivers = Array.Empty<Terrain?>();
        Triangles = 0;
    }

    public void Dispose()
    {
        Clear();
        if (_material)
            UnityEngine.Object.Destroy(_material);
        _material = null;
        _materialInitialized = false;
    }
}
