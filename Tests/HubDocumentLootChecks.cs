using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Hub;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace SeasonalPerks.Tests;

internal static class HubDocumentLootChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "hub-gameplay.json"));
        var catalogue = JObject.Parse(source);
        var rules = new HubDocumentLoot(source);
        var documents = catalogue["Documents"]!.Select(d => (string)d["itemId"]!).ToArray();
        foreach (var map in ((JObject)catalogue["CapturedMapCaps"]!).Properties())
        {
            var expected = map.Value.ToDictionary(c => (string)c["TemplateId"]!, c => (int)c["Value"]!);
            var pool = rules.Pool(map.Name.ToUpperInvariant(), documents, 8);
            check(
                pool.Count == expected.Count && pool.All(p => expected[p.Key] == p.Value),
                "Map-specific document types and caps: " + map.Name
            );
            for (var repeat = 0; repeat < 20; repeat++)
            {
                var points = Enumerable.Range(0, 20).Select(_ => Point()).ToArray();
                var placed = rules.Place(points, map.Name, documents, 8, _ => true);
                check(placed.Count == 8, "Available loose points fill the raid limit: " + map.Name);
                check(
                    placed.GroupBy(d => d.Template.ToString()).All(g => expected.TryGetValue(g.Key, out var cap) && g.Count() <= cap),
                    "Random placement never leaks types or exceeds map caps: " + map.Name
                );
                check(
                    placed.All(d =>
                        d.ParentId == null
                        && d.SlotId == null
                        && d.Location == null
                        && d.Upd?.StackObjectsCount == 1
                        && d.Upd.SpawnedInSession == true
                    ),
                    "Documents are standalone found-in-raid roots"
                );
                check(points.Select(p => p.Root).Distinct().Count() == points.Length, "Loose roots retain distinct identities");
            }
        }
        foreach (var map in new string?[] { "unknown", "hideout", "", null })
        {
            check(rules.Pool(map, documents, 8).Count == 0, "Unlisted maps have no captured document spawns");
        }
        var customs = rules.Pool("bigmap", documents, 8);
        check(
            customs.Keys.ToHashSet().SetEquals(["6a3181f178450ec91c0ea1aa", "6a31807f17005505b70d5827"]),
            "Customs permits only personal and financial documents"
        );
        check(
            rules.Place(Enumerable.Range(0, 20).Select(_ => Point()), "bigmap", [customs.Keys.First()], 8, _ => true).Count == 7,
            "Single available type stops at its captured cap"
        );
        check(
            rules.Place([Point()], "bigmap", documents, 0, _ => true).Count == 0,
            "Disabled spawning and exhausted allowance place nothing"
        );
        check(rules.Place([Point(), Point()], "bigmap", documents, 1, _ => true).Count == 1, "Remaining allowance limits placement");
        check(
            rules.Place([Point()], "bigmap", documents, 8, _ => true).Count == 1,
            "Insufficient loose points never fall back to containers"
        );

        var container = Point();
        container.IsContainer = true;
        var mandatory = Point();
        mandatory.IsAlwaysSpawn = true;
        var child = Point();
        child.Items!.First().ParentId = new MongoId().ToString();
        var attached = Point();
        attached.Items = [.. attached.Items!, new SptLootItem { Id = new MongoId(), Template = new MongoId() }];
        var missingRoot = Point();
        missingRoot.Root = new MongoId().ToString();
        var missingPosition = Point();
        missingPosition.Position = null;
        var unknownKind = Point();
        unknownKind.IsContainer = null;
        var protectedPoints = new[] { container, mandatory, child, attached, missingRoot, missingPosition, unknownKind };
        var originalRoots = protectedPoints.Select(p => p.Root).ToArray();
        var originalContents = protectedPoints.Select(p => p.Items).ToArray();
        check(
            rules.Place(protectedPoints, "bigmap", documents, 8, _ => true).Count == 0,
            "Containers, mandatory spawns and malformed or attached loot are excluded"
        );
        check(
            protectedPoints.Select(p => p.Root).SequenceEqual(originalRoots)
                && protectedPoints.Select(p => p.Items).SequenceEqual(originalContents),
            "Excluded loot remains untouched"
        );
        check(rules.Place([Point()], "bigmap", documents, 8, _ => false).Count == 0, "Quest and unsupported templates cannot be selected");
        var loose = Point();
        var transform = (loose.Position, loose.Rotation, loose.Id, loose.UseGravity, loose.GroupPositions);
        rules.Place([loose], "bigmap", documents, 8, _ => true);
        check(
            (loose.Position, loose.Rotation, loose.Id, loose.UseGravity, loose.GroupPositions).Equals(transform)
                && loose.Root == loose.Items!.Single().Id.ToString(),
            "Replacement preserves the native spawn point and world transform"
        );
        const string custom = "aaaaaaaaaaaaaaaaaaaaaaaa";
        check(rules.Pool("woods", [custom], 3)[custom] == 3, "Custom document templates retain their configured limits");
    }

    private static SpawnpointTemplate Point()
    {
        var id = new MongoId();
        return new SpawnpointTemplate
        {
            Id = Guid.NewGuid().ToString(),
            Root = id.ToString(),
            IsContainer = false,
            Position = new Vector3
            {
                X = 1,
                Y = 2,
                Z = 3,
            },
            Rotation = new Vector3(),
            Items = [new SptLootItem { Id = id, Template = new MongoId() }],
        };
    }
}
