using System.Security.Cryptography;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Hub;

namespace WTT.Campaigns.Server.Hub;

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
        var available = documents.Where(t => templates.Items.ContainsKey(new MongoId(t)));
        var spawned = _documentLoot.Place(
            loot.Loot!,
            request.Location,
            available,
            count,
            template =>
                templates.Items.TryGetValue(template, out var item)
                && item.Properties?.QuestItem != true
                && WTT.Campaigns.Server.Effects.TemplateFilters.Ancestors(templates, template)
                    .Any(parent => parent is "5448eb774bdc2d0a728b4567" or "567849dd4bdc2d150f8b456e")
        );
        foreach (var item in spawned)
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
