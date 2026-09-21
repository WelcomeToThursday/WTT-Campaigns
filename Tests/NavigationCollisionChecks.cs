using System.Numerics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring.Navigation;

namespace WTT.Campaigns.Tests;

internal static class NavigationCollisionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "campaigns-collision-" + Guid.NewGuid().ToString("N"));
        var paths = new[] { "globalgamemanagers", "level63", "sharedassets63.assets", "Managed/UnityEngine.TerrainPhysicsModule.dll" };
        Directory.CreateDirectory(Path.Combine(folder, "Managed"));
        try
        {
            var catalog = new NavigationCollisionCatalog
            {
                Schema = 1,
                Files = paths
                    .Select(path =>
                    {
                        File.WriteAllText(Path.Combine(folder, path), path);
                        return new NavigationCollisionCatalog.SourceFile
                        {
                            Path = path,
                            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, path)))),
                        };
                    })
                    .ToArray(),
                Terrains = new[]
                {
                    new NavigationCollisionCatalog.TerrainEntry
                    {
                        ScenePath = "Assets/Pilot.unity",
                        BuildIndex = 63,
                        DataName = "Tile",
                        DataFile = "sharedassets63.assets",
                        Resolution = 1025,
                        TreeCount = 12,
                        Size = new[] { 700f, 180f, 700f },
                        EnableTreeColliders = false,
                        Hierarchy = new[]
                        {
                            new NavigationCollisionCatalog.Node
                            {
                                Name = "terrain",
                                Sibling = -1,
                                Position = new[] { 0f, 0f, 0f },
                                Rotation = new[] { 0f, 0f, 0f, 1f },
                                Scale = new[] { 1f, 1f, 1f },
                            },
                        },
                    },
                },
            };
            check(catalog.VerifyFiles(folder) == "", "Native collision catalogue verifies exact scene, terrain data and bindings files");
            NavigationCollisionCatalog Copy() =>
                JsonConvert.DeserializeObject<NavigationCollisionCatalog>(JsonConvert.SerializeObject(catalog))!;
            var changed = Copy();
            changed.Terrains[0].EnableTreeColliders = null;
            check(changed.Validate().Length > 0, "Missing tree collision flag is unknown, never disabled");
            changed = Copy();
            changed.Schema++;
            check(changed.Validate().Length > 0, "Future collision evidence schema fails closed");
            changed = Copy();
            changed.Files = changed.Files.Skip(1).ToArray();
            check(changed.Validate().Length > 0, "Terrain evidence requires native scene mapping provenance");
            changed = Copy();
            changed.Files[0].Path = "../other-game/globalgamemanagers";
            check(changed.Validate().Length > 0, "Collision file verification cannot escape the selected installation");
            changed = Copy();
            changed.Terrains = new[] { changed.Terrains[0], changed.Terrains[0] };
            check(changed.Validate().Length > 0, "Ambiguous terrain owners fail closed");
            changed = Copy();
            changed.Terrains[0].DataFile = "unknown.assets";
            check(changed.Validate().Length > 0, "Terrain evidence cannot cite unhashed native data");
            changed = Copy();
            changed.Terrains[0].Size[0] = float.NaN;
            check(changed.Validate().Length > 0, "Nonfinite native terrain metadata is rejected");
            File.AppendAllText(Path.Combine(folder, "level63"), "changed");
            check(
                catalog.VerifyFiles(folder).Contains("level63"),
                "Changed native scene invalidates terrain evidence with a specific file reason"
            );
        }
        finally
        {
            foreach (var path in paths)
                File.Delete(Path.Combine(folder, path));
            Directory.Delete(Path.Combine(folder, "Managed"));
            Directory.Delete(folder);
        }

        var source = new Vector3(12, 3, -5);
        var size = new Vector3(2, 1, 3);
        var box = NavigationRestrictionGeometry.ConvexExclusion(source, size, .3f, 1.7f, .38f, .1f);
        var min = box.Center - box.Size / 2;
        var max = box.Center + box.Size / 2;
        check(min.Y > 0, "High suspended convex geometry does not exclude a floor below standing clearance");
        var random = new Random(7341);
        for (var i = 0; i < 256; i++)
        {
            // Sample feet positions for agents whose bodies intersect the physical box,
            // including side clearance and feet below a floating collider.
            var foot =
                source
                + new Vector3(
                    (float)(random.NextDouble() * 2 - 1) * (size.X / 2 + .3f),
                    (float)random.NextDouble() * (size.Y + 1.7f) - size.Y / 2 - 1.7f,
                    (float)(random.NextDouble() * 2 - 1) * (size.Z / 2 + .3f)
                );
            check(
                foot.X >= min.X && foot.X <= max.X && foot.Y >= min.Y && foot.Y <= max.Y && foot.Z >= min.Z && foot.Z <= max.Z,
                "Conservative convex exclusion covers standing-body collisions without requiring a guessed hull"
            );
        }
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            var rejected = false;
            try
            {
                NavigationRestrictionGeometry.ConvexExclusion(source, size, .3f, 1.7f, .38f, invalid);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, "Invalid bake resolution cannot create a convex exclusion");
        }
    }
}
