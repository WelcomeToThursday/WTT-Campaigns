using System.Numerics;
using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class ScenePickingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var a = new Vector3(-2, -2, 5);
        var b = new Vector3(2, -2, 5);
        var c = new Vector3(-2, 2, 5);
        var distance = 10f;
        check(
            !ScenePickGeometry.Triangle(new(1, 1, 0), Vector3.UnitZ, a, b, c, ref distance) && distance == 10,
            "Empty space inside a mesh bounding box cannot steal the body hit"
        );
        check(
            ScenePickGeometry.Triangle(new(-1, -1, 0), Vector3.UnitZ, a, b, c, ref distance) && distance == 5,
            "A visible surface in front of the physics hit is selectable"
        );
        check(
            !ScenePickGeometry.Triangle(new(-1, -1, 0), Vector3.UnitZ, a * 2, b * 2, c * 2, ref distance) && distance == 5,
            "A surface behind the current selection cannot steal it"
        );
        distance = 10;
        check(
            ScenePickGeometry.Triangle(new(-1, -1, 0), Vector3.UnitZ * 2, c, b, a, ref distance) && distance == 2.5f,
            "Mesh picking preserves world distance under scale and accepts reversed winding"
        );
        distance = 10;
        check(
            !ScenePickGeometry.Triangle(new(-1, -1, 0), -Vector3.UnitZ, a, b, c, ref distance),
            "Geometry behind the camera is not selectable"
        );
        check(!ScenePickGeometry.Triangle(Vector3.Zero, Vector3.UnitX, a, b, c, ref distance), "Parallel rays do not select a triangle");
        check(
            !ScenePickGeometry.Triangle(Vector3.Zero, Vector3.UnitZ, a, a, a, ref distance),
            "Degenerate triangles do not select an object"
        );
    }
}
