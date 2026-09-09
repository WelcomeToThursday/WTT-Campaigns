using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SeasonalPerks.Server.Hub;

internal sealed class HubDocumentLoot
{
    private readonly Dictionary<string, Dictionary<string, int>> _maps;
    private readonly HashSet<string> _capturedDocuments;

    internal HubDocumentLoot(string catalogueJson)
    {
        var catalogue = JObject.Parse(catalogueJson);
        _capturedDocuments = catalogue["Documents"]!.Select(d => (string)d["itemId"]!).ToHashSet();
        _maps = ((JObject)catalogue["CapturedMapCaps"]!)
            .Properties()
            .ToDictionary(
                map => map.Name,
                map => map.Value.ToDictionary(cap => (string)cap["TemplateId"]!, cap => (int)cap["Value"]!),
                StringComparer.OrdinalIgnoreCase
            );
    }

    internal Dictionary<string, int> Pool(string? location, IEnumerable<string> documents, int limit)
    {
        var map = _maps.GetValueOrDefault(location ?? "");
        return documents
            .Distinct()
            .ToDictionary(
                document => document,
                // Original documents keep their captured map restrictions even in a custom season.
                // Newly authored document templates retain the season's configurable map limits.
                document => _capturedDocuments.Contains(document) ? Math.Min(limit, map?.GetValueOrDefault(document) ?? 0) : limit
            )
            .Where(pair => pair.Value > 0)
            .ToDictionary();
    }

    internal List<SptLootItem> Place(
        IEnumerable<SpawnpointTemplate> loot,
        string? location,
        IEnumerable<string> documents,
        int limit,
        Func<MongoId, bool> eligibleTemplate
    )
    {
        var pool = Pool(location, documents, limit);
        var placed = new List<SptLootItem>();
        foreach (var point in loot.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)))
        {
            if (placed.Count >= limit || pool.Count == 0)
            {
                break;
            }
            var items = point.Items?.ToArray();
            if (
                point.IsContainer != false
                || point.IsAlwaysSpawn == true
                || point.Position == null
                || point.Rotation == null
                || items?.Length != 1
                || items[0].Id.ToString() != point.Root
                || !string.IsNullOrEmpty(items[0].ParentId)
                || !eligibleTemplate(items[0].Template)
            )
            {
                continue;
            }

            var template = pool.Keys.ElementAt(RandomNumberGenerator.GetInt32(pool.Count));
            var document = new SptLootItem
            {
                Id = new MongoId(),
                Template = new MongoId(template),
                Upd = new Upd { StackObjectsCount = 1, SpawnedInSession = true },
            };
            // Replace one ordinary loose item at its generated world transform. Adding another
            // spawn here would overlap loot; container roots and their contents are never changed.
            point.Root = document.Id.ToString();
            point.Items = [document];
            placed.Add(document);
            if (--pool[template] == 0)
            {
                pool.Remove(template);
            }
        }
        return placed;
    }
}
