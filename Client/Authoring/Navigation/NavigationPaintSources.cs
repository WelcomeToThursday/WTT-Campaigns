using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using V = System.Numerics.Vector3;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal sealed class NavigationPaintSources : IDisposable
{
    private readonly List<Mesh> _meshes = new();
    internal readonly List<NavMeshBuildSource> Sources = new();
    private readonly Dictionary<(int, int), List<MapNavigationCell>> _cells = new();
    private readonly Dictionary<MapNavigationCell, List<(V, V, V)>> _native = new();
    private readonly Dictionary<int, List<Vector3>> _vertices = new();
    private readonly Bounds _bounds;
    private readonly float _height;
    private int _total;

    internal NavigationPaintSources(MapNavigationRecipe recipe, Bounds bounds, float height)
    {
        _bounds = bounds;
        _height = height;
        foreach (var cell in recipe.Cells.AsValueEnumerable().Where(c => c.Mode == "Add"))
        {
            if (!_cells.TryGetValue((cell.X, cell.Z), out var floors))
                _cells[(cell.X, cell.Z)] = floors = new();
            floors.Add(cell);
            _native[cell] = new();
        }
    }

    internal static Bounds Region(MapNavigationRecipe recipe, float height)
    {
        if (recipe.Cells.Count == 0)
            throw new InvalidOperationException("Paint an Add or Block area first.");
        var first = recipe.Cells[0];
        var bounds = new Bounds(new Vector3(first.X, first.Y, first.Z), Vector3.zero);
        foreach (var c in recipe.Cells)
        {
            bounds.Encapsulate(new Vector3(c.X, c.Y - .6f, c.Z));
            bounds.Encapsulate(new Vector3(c.X + 1, c.Y + height + .6f, c.Z + 1));
        }
        // A single explicit local bake. Distant footprints must not accidentally
        // request a terrain-sized intermediate mesh or a full-map collection.
        if (bounds.size.x > 128 || bounds.size.z > 128 || bounds.size.y > 64)
            throw new InvalidOperationException("Keep this layout's manual navigation edits within a 128 × 128 × 64 m region.");
        return bounds;
    }

    internal async UniTask Build(NavigationSurvey survey, NavMeshTriangulation native, CancellationToken token)
    {
        for (var i = 0; i < native.indices.Length; i += 3)
        {
            var a = N(native.vertices[native.indices[i]]);
            var b = N(native.vertices[native.indices[i + 1]]);
            var c = N(native.vertices[native.indices[i + 2]]);
            Visit(
                a,
                b,
                c,
                cell =>
                {
                    var floor = NavigationPaintGeometry.Box(
                        a,
                        b,
                        c,
                        new(cell.X, cell.Y - .6f, cell.Z),
                        new(cell.X + 1, cell.Y + .6f, cell.Z + 1)
                    );
                    for (var index = 1; index + 1 < floor.Count; index++)
                        _native[cell].Add((floor[0], floor[index], floor[index + 1]));
                }
            );
            if (i % 12288 == 0)
                await UniTask.NextFrame(cancellationToken: token);
        }
        foreach (var source in survey.Sources)
        {
            token.ThrowIfCancellationRequested();
            if (source.shape == NavMeshBuildSourceShape.ModifierBox)
            {
                Sources.Add(source);
                continue;
            }
            if (source.shape is NavMeshBuildSourceShape.Sphere or NavMeshBuildSourceShape.Capsule)
            {
                // Keep actual curved collision as a restriction, never a guessed
                // walkable proxy or an unpainted source of new floor.
                var restriction = source;
                restriction.area = 1;
                Sources.Add(restriction);
                continue;
            }
            if (source.sourceObject is Mesh mesh)
            {
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                for (var i = 0; i < indices.Length; i += 3)
                {
                    Triangle(
                        source.transform.MultiplyPoint3x4(vertices[indices[i]]),
                        source.transform.MultiplyPoint3x4(vertices[indices[i + 1]]),
                        source.transform.MultiplyPoint3x4(vertices[indices[i + 2]]),
                        source.area
                    );
                    if (i % 3072 == 0)
                        await UniTask.NextFrame(cancellationToken: token);
                }
            }
            else if (source.sourceObject is TerrainData terrain)
            {
                var inverse = source.transform.inverse;
                var localMin = inverse.MultiplyPoint3x4(_bounds.min);
                var localMax = inverse.MultiplyPoint3x4(_bounds.max);
                var scale = terrain.heightmapScale;
                var x0 = Mathf.Clamp(Mathf.FloorToInt(localMin.x / scale.x), 0, terrain.heightmapResolution - 2);
                var z0 = Mathf.Clamp(Mathf.FloorToInt(localMin.z / scale.z), 0, terrain.heightmapResolution - 2);
                var x1 = Mathf.Clamp(Mathf.CeilToInt(localMax.x / scale.x), x0 + 1, terrain.heightmapResolution - 1);
                var z1 = Mathf.Clamp(Mathf.CeilToInt(localMax.z / scale.z), z0 + 1, terrain.heightmapResolution - 1);
                var heights = terrain.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
                Vector3 Point(int x, int z) =>
                    source.transform.MultiplyPoint3x4(new Vector3(x * scale.x, heights[z - z0, x - x0] * scale.y, z * scale.z));
                for (var z = z0; z < z1; z++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        if (terrain.IsHole(x, z))
                            continue;
                        var a = Point(x, z);
                        var b = Point(x + 1, z);
                        var c = Point(x + 1, z + 1);
                        var d = Point(x, z + 1);
                        Triangle(a, d, c, source.area);
                        Triangle(a, c, b, source.area);
                    }
                    await UniTask.NextFrame(cancellationToken: token);
                }
            }
            else if (source.shape == NavMeshBuildSourceShape.Box)
            {
                var p = new Vector3[8];
                for (var i = 0; i < 8; i++)
                    p[i] = source.transform.MultiplyPoint3x4(
                        Vector3.Scale(source.size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))
                    );
                var faces = new[]
                {
                    0,
                    2,
                    3,
                    0,
                    3,
                    1,
                    4,
                    5,
                    7,
                    4,
                    7,
                    6,
                    0,
                    4,
                    6,
                    0,
                    6,
                    2,
                    1,
                    3,
                    7,
                    1,
                    7,
                    5,
                    0,
                    1,
                    5,
                    0,
                    5,
                    4,
                    2,
                    6,
                    7,
                    2,
                    7,
                    3,
                };
                for (var i = 0; i < faces.Length; i += 3)
                    Triangle(p[faces[i]], p[faces[i + 1]], p[faces[i + 2]], source.area);
            }
            else
                throw new InvalidOperationException(
                    "Painted-area geometry contains an unsupported " + source.shape + " source: " + source.component?.name
                );
        }
        foreach (var pair in _vertices)
        {
            if (pair.Value.Count == 0)
                continue;
            var mesh = new Mesh { name = "CampaignEditor painted geometry", indexFormat = IndexFormat.UInt32 };
            _meshes.Add(mesh);
            mesh.SetVertices(pair.Value);
            mesh.triangles = ValueEnumerable.Range(0, pair.Value.Count).ToArray();
            mesh.RecalculateBounds();
            Sources.Add(
                new()
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = mesh,
                    transform = Matrix4x4.identity,
                    area = pair.Key,
                }
            );
        }
        if (_meshes.Count == 0 && _cells.Count > 0)
            throw new InvalidOperationException(
                "No new physical floor remains inside the Add paint. Existing navigation is preserved, not duplicated."
            );
    }

    private void Triangle(Vector3 av, Vector3 bv, Vector3 cv, int area)
    {
        var a = N(av);
        var b = N(bv);
        var c = N(cv);
        Visit(
            a,
            b,
            c,
            cell =>
            {
                var polygon = NavigationPaintGeometry.Box(
                    a,
                    b,
                    c,
                    new V(cell.X, cell.Y - .6f, cell.Z),
                    new V(cell.X + 1, cell.Y + _height + .6f, cell.Z + 1)
                );
                if (polygon.Count < 3)
                    return;
                var pieces = new List<List<V>> { polygon };
                foreach (var triangle in _native[cell])
                {
                    var next = new List<List<V>>();
                    foreach (var part in pieces)
                        next.AddRange(NavigationPaintGeometry.Subtract(part, triangle.Item1, triangle.Item2, triangle.Item3));
                    pieces = next;
                    if (pieces.Count == 0)
                        break;
                    if (pieces.Count > 4096)
                        throw new InvalidOperationException(
                            "Paint boundary is too complex in this cell. Erase the overlap with native navigation."
                        );
                }
                foreach (var piece in pieces)
                {
                    // Ceilings and another floor remain collision, not additional
                    // walking surfaces just because their X/Z lies under a brush.
                    Append(NavigationPaintGeometry.Clip(piece, -V.UnitY, -(cell.Y + .6f)), area);
                    Append(NavigationPaintGeometry.Clip(piece, V.UnitY, cell.Y + .6f), 1);
                }
            }
        );
    }

    private void Append(List<V> piece, int area)
    {
        if (!_vertices.TryGetValue(area, out var vertices))
            _vertices[area] = vertices = new();
        for (var i = 1; i + 1 < piece.Count; i++)
        {
            if (V.Cross(piece[i] - piece[0], piece[i + 1] - piece[0]).LengthSquared() < .00000001f)
                continue;
            vertices.Add(U(piece[0]));
            vertices.Add(U(piece[i]));
            vertices.Add(U(piece[i + 1]));
            if ((_total += 3) > 1500000)
                throw new InvalidOperationException("Painted geometry exceeds the 500,000 triangle preview limit.");
        }
    }

    private void Visit(V a, V b, V c, Action<MapNavigationCell> visit)
    {
        var min = V.Min(a, V.Min(b, c));
        var max = V.Max(a, V.Max(b, c));
        var x0 = Math.Max((int)Math.Floor(min.X), (int)Math.Floor(_bounds.min.x));
        var x1 = Math.Min((int)Math.Floor(max.X), (int)Math.Floor(_bounds.max.x));
        var z0 = Math.Max((int)Math.Floor(min.Z), (int)Math.Floor(_bounds.min.z));
        var z1 = Math.Min((int)Math.Floor(max.Z), (int)Math.Floor(_bounds.max.z));
        for (var z = z0; z <= z1; z++)
        for (var x = x0; x <= x1; x++)
            if (_cells.TryGetValue((x, z), out var floors))
                foreach (var cell in floors)
                    if (max.Y >= cell.Y - .6f && min.Y <= cell.Y + _height + .6f)
                        visit(cell);
    }

    private static V N(Vector3 p) => new(p.x, p.y, p.z);

    private static Vector3 U(V p) => new(p.X, p.Y, p.Z);

    public void Dispose()
    {
        foreach (var mesh in _meshes)
            if (mesh)
                UnityEngine.Object.Destroy(mesh);
        _meshes.Clear();
    }
}
