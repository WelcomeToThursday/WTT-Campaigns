using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Hub;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace SeasonalPerks.Server.Hub;

public sealed partial class HubGameplay
{
    public async Task StartRaid(string sessionId, StartLocalRaidRequestData request, StartLocalRaidResponseData response)
    {
        if (_runtimes != null)
        {
            await ForSession(sessionId).StartRaid(sessionId, request, response);
            return;
        }
        if (!_ready || !seasons.IsSeasonal(sessionId) || !string.Equals(request.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        if (seasons.EffectiveId(root) != sessionId || response.ServerId == null || response.LocationLoot?.Loot == null)
        {
            return;
        }

        var id = new MongoId(sessionId);
        var original = saves.GetProfile(id);
        var staged = cloner.Clone(original)!;
        var pmc = staged.CharacterData!.PmcData!;
        var state = Progress(pmc);
        if (state.Raids.ContainsKey(response.ServerId))
        {
            return;
        }
        foreach (var abandoned in state.Raids.Values.Where(r => !r.Finished))
        {
            abandoned.Finished = true;
        }

        var raid = new HubRaid();
        var loot = cloner.Clone(response.LocationLoot)!;
        var documents = Documents.Values.ToArray();
        foreach (var item in pmc.Inventory?.Items ?? [])
        {
            if (documents.Contains(item.Template.ToString()))
            {
                HubProvenance.Register(
                    raid,
                    item.Id.ToString(),
                    item.Template.ToString(),
                    checked((int)(item.Upd?.StackObjectsCount ?? 1)),
                    false
                );
            }
        }
        var count = Math.Min(
            Configuration.MapCounts.GetValueOrDefault(request.Location ?? "", Configuration.DocumentsPerRaid),
            HubRules.Remaining(state, Now, _presentation.DocumentLimit, _presentation.WindowSeconds)
        );
        // A complete dependency set keeps all eight types equally likely.
        if (documents.All(t => templates.Items.ContainsKey(new MongoId(t))))
        {
            foreach (
                var container in loot.Loot!.Where(l => l.IsContainer == true).OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
            )
            {
                if (count <= 0)
                {
                    break;
                }

                var contents = container.Items?.ToList();
                var host = contents?.FirstOrDefault(i => i.Id.ToString() == container.Root);
                if (host == null || !templates.Items.TryGetValue(host.Template, out var template))
                {
                    continue;
                }

                var name = template.Name?.ToLowerInvariant() ?? "";
                if (!new[] { "jacket", "drawer", "safe", "duffle", "duffel", "sportbag" }.Any(name.Contains))
                {
                    continue;
                }

                var tpl = documents[RandomNumberGenerator.GetInt32(documents.Length)];
                var item = new SptLootItem
                {
                    Id = new MongoId(),
                    Template = new MongoId(tpl),
                    ParentId = host.Id.ToString(),
                    Upd = new Upd { StackObjectsCount = 1, SpawnedInSession = true },
                };
                if (!PlaceDocument(host, item, contents!))
                {
                    continue;
                }

                contents!.Add(item);
                container.Items = contents;
                count--;
            }
        }
        foreach (var item in loot.Loot!.SelectMany(l => l.Items ?? []))
        {
            if (documents.Contains(item.Template.ToString()))
            {
                HubProvenance.Register(
                    raid,
                    item.Id.ToString(),
                    item.Template.ToString(),
                    checked((int)(item.Upd?.StackObjectsCount ?? 1)),
                    true
                );
            }
        }

        state.Raids[response.ServerId] = raid;
        state.Revision++;
        Store(pmc, state);
        await Commit(id, original, staged);
        response.LocationLoot = loot;
    }

    private bool PlaceDocument(Item host, Item document, List<SptLootItem> contents)
    {
        var grids = templates.Items[host.Template].Properties?.Grids;
        var size = inventory.GetItemSize(document.Template, document.Id, [document]);
        foreach (var grid in grids ?? [])
        {
            var ancestors = new HashSet<MongoId>();
            var current = document.Template;
            while (ancestors.Add(current) && templates.Items.TryGetValue(current, out var ancestor))
            {
                current = ancestor.Parent;
            }
            var filters = grid.Properties?.Filters?.ToArray() ?? [];
            if (
                filters.Length > 0
                && !filters.Any(f =>
                    f.Locked != true
                    && (f.Filter?.Count is not > 0 || f.Filter.Overlaps(ancestors))
                    && f.ExcludedFilter?.Overlaps(ancestors) != true
                )
            )
            {
                continue;
            }
            var width = grid.Properties?.CellsH ?? 0;
            var height = grid.Properties?.CellsV ?? 0;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var occupied = new bool[width, height];
            var valid = true;
            foreach (var child in contents.Where(i => i.ParentId == host.Id.ToString() && i.SlotId == grid.Name))
            {
                if (child.Location == null)
                {
                    valid = false;
                    break;
                }
                var location = JObject.Parse(json.Serialize(child.Location)!);
                var x = (int?)location["x"] ?? -1;
                var y = (int?)location["y"] ?? -1;
                var childSize = inventory.GetItemSize(child.Template, child.Id, contents);
                var rotated = location["r"]?.ToString() is "1" or "Vertical";
                var w = rotated ? childSize.Item2 : childSize.Item1;
                var h = rotated ? childSize.Item1 : childSize.Item2;
                if (x < 0 || y < 0 || x + w > width || y + h > height)
                {
                    valid = false;
                    break;
                }
                for (var xx = x; xx < x + w; xx++)
                {
                    for (var yy = y; yy < y + h; yy++)
                    {
                        occupied[xx, yy] = true;
                    }
                }
            }
            if (!valid)
            {
                continue;
            }

            for (var y = 0; y <= height - size.Item2; y++)
            {
                for (var x = 0; x <= width - size.Item1; x++)
                {
                    var free = true;
                    for (var xx = x; xx < x + size.Item1; xx++)
                    {
                        for (var yy = y; yy < y + size.Item2; yy++)
                        {
                            free &= !occupied[xx, yy];
                        }
                    }

                    if (!free)
                    {
                        continue;
                    }

                    document.SlotId = grid.Name;
                    document.Location = new ItemLocation
                    {
                        X = x,
                        Y = y,
                        IsSearched = false,
                    };
                    return true;
                }
            }
        }
        return false;
    }

    public async Task<HubResult> Pickup(string sessionId, HubRequest request)
    {
        if (_runtimes != null)
        {
            return await ForSession(sessionId).Pickup(sessionId, request);
        }
        ValidateSeasonRequest(request);
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var original = Active(root);
        if (seasons.EffectiveId(root) != sessionId)
        {
            throw new InvalidOperationException("The active character changed.");
        }

        var staged = cloner.Clone(original)!;
        var pmc = staged.CharacterData!.PmcData!;
        var state = Progress(pmc);
        var raid = state.Raids.Values.SingleOrDefault(r => !r.Finished && r.Stacks.ContainsKey(request.ItemId));
        if (raid == null)
        {
            return new HubResult { Ignored = true, Message = "This is not a new raid document." };
        }

        if (!Guid.TryParseExact(request.OperationId, "N", out _))
        {
            throw new InvalidOperationException("Invalid document operation identifier.");
        }
        var fingerprint = json.Serialize(
            new
            {
                request.ItemId,
                request.TargetId,
                request.Count,
                request.Split,
                request.PickedUp,
            }
        )!;
        if (raid.Operations.TryGetValue(request.OperationId, out var previous))
        {
            if (previous != fingerprint)
            {
                throw new InvalidOperationException("Document operation identifier was reused.");
            }
            return new HubResult { Committed = true };
        }
        var stackId = request.ItemId;
        if (request.TargetId.Length > 0)
        {
            if (!MongoId.IsValidMongoId(request.TargetId))
            {
                throw new InvalidOperationException("Invalid destination item identifier.");
            }
            HubProvenance.Transfer(raid, request.ItemId, request.TargetId, request.Count, request.Split);
            stackId = request.TargetId;
        }
        var accepted = true;
        if (request.PickedUp)
        {
            foreach (var unit in raid.Stacks[stackId].Units.Where(raid.Spawned.ContainsKey))
            {
                accepted &= HubRules.Pickup(state, raid, unit, Now, _presentation.DocumentLimit, _presentation.WindowSeconds);
            }
        }
        raid.Operations.Add(request.OperationId, fingerprint);
        state.Revision++;
        Store(pmc, state);
        await Commit(new MongoId(sessionId), original, staged);
        return new HubResult { Committed = true, Message = accepted ? "Document recorded." : "Document allowance exhausted." };
    }

    public bool RaidFinished(string sessionId, string? raidId)
    {
        if (_runtimes != null)
        {
            return ForSession(sessionId).RaidFinished(sessionId, raidId);
        }
        return _ready
            && seasons.IsSeasonal(sessionId)
            && raidId != null
            && Progress(saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!).Raids.TryGetValue(raidId, out var raid)
            && raid.Finished;
    }

    public async Task FinishRaid(string sessionId, EndLocalRaidRequestData request, bool leaseHeld = false)
    {
        if (_runtimes != null)
        {
            await ForSession(sessionId).FinishRaid(sessionId, request, leaseHeld);
            return;
        }
        if (!_ready || !seasons.IsSeasonal(sessionId) || request.ServerId == null)
        {
            return;
        }

        var root = seasons.ResolveRoot(sessionId);
        using var lease = leaseHeld ? null : seasons.Enter(root);
        var id = new MongoId(sessionId);
        var original = saves.GetProfile(id);
        var staged = cloner.Clone(original)!;
        var pmc = staged.CharacterData!.PmcData!;
        var state = Progress(pmc);
        if (!state.Raids.TryGetValue(request.ServerId, out var raid) || raid.Finished)
        {
            return;
        }

        var survived = request.Results?.Result?.ToString().ToLowerInvariant() is "survived" or "runner" or "runthrough";
        var extracted = (pmc.Inventory?.Items ?? [])
            .Where(i => raid.Stacks.TryGetValue(i.Id.ToString(), out var stack) && stack.Template == i.Template.ToString())
            .ToList();
        var extractedUnits = new List<string>();
        foreach (var item in extracted)
        {
            var units = raid.Stacks[item.Id.ToString()].Units;
            var count = checked((int)(item.Upd?.StackObjectsCount ?? 1));
            if (count != units.Count)
            {
                // An unreported stack operation must not turn an existing document into a new acquisition.
                continue;
            }
            var rejected = 0;
            foreach (var unit in units.Where(raid.Spawned.ContainsKey))
            {
                if (HubRules.Pickup(state, raid, unit, Now, _presentation.DocumentLimit, _presentation.WindowSeconds))
                {
                    extractedUnits.Add(unit);
                }
                else
                {
                    rejected++;
                }
            }
            if (rejected > 0)
            {
                item.Upd ??= new Upd();
                item.Upd.StackObjectsCount = count - rejected;
                if (item.Upd.StackObjectsCount == 0)
                {
                    pmc.Inventory!.Items!.Remove(item);
                }
            }
        }

        HubRules.Finish(
            state,
            raid,
            extractedUnits,
            survived,
            () => RandomNumberGenerator.GetInt32(100),
            Configuration.ClassifiedChancePercent
        );
        state.Revision++;
        Store(pmc, state);
        await Commit(id, original, staged);
    }
}
