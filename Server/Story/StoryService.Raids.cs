using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private void RecordRaidNativeChanges(StoryRaid raid, PmcData before, PmcData after)
    {
        // Raid reports contain the player's local progress, not the lobby's native reward updates.
        raid.Experience += (after.Info?.Experience ?? 0) - (before.Info?.Experience ?? 0);
        foreach (var skill in after.Skills?.Common ?? [])
        {
            var difference = skill.Progress - (before.Skills?.Common?.FirstOrDefault(s => s.Id == skill.Id)?.Progress ?? 0);
            raid.Skills[skill.Id.ToString()] = raid.Skills.GetValueOrDefault(skill.Id.ToString()) + difference;
        }
        foreach (var trader in after.TradersInfo ?? [])
        {
            var difference = (trader.Value.Standing ?? 0) - (before.TradersInfo?.GetValueOrDefault(trader.Key)?.Standing ?? 0);
            raid.Standing[trader.Key.ToString()] = raid.Standing.GetValueOrDefault(trader.Key.ToString()) + difference;
        }
        foreach (var quest in after.Quests ?? [])
        {
            var previous = before.Quests?.FirstOrDefault(q => q.QId == quest.QId);
            if (previous?.Status != quest.Status)
            {
                raid.QuestTransitions[quest.QId.ToString()] = json.Serialize(quest)!;
            }
        }
    }

    private void MergeRaidNativeChanges(StoryRaid raid, PmcData pmc)
    {
        pmc.Info!.Experience = checked((pmc.Info.Experience ?? 0) + (int)raid.Experience);
        foreach (var skill in pmc.Skills?.Common ?? [])
        {
            skill.Progress += raid.Skills.GetValueOrDefault(skill.Id.ToString());
        }
        foreach (var trader in pmc.TradersInfo ?? [])
        {
            trader.Value.Standing = (trader.Value.Standing ?? 0) + raid.Standing.GetValueOrDefault(trader.Key.ToString());
        }
        pmc.Quests ??= [];
        foreach (var transition in raid.QuestTransitions)
        {
            var committed = json.Deserialize<QuestStatus>(transition.Value)!;
            var reported = pmc.Quests.FirstOrDefault(q => q.QId == committed.QId);
            if (reported == null)
            {
                pmc.Quests.Add(committed);
            }
            else if (
                committed.Status is QuestStatusEnum.Success or QuestStatusEnum.Fail
                || reported.Status is QuestStatusEnum.Locked or QuestStatusEnum.AvailableForStart
            )
            {
                reported.Status = committed.Status;
                reported.StatusTimers = committed.StatusTimers;
            }
        }
    }

    public async Task NewSession(string sessionId)
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        await ResetSessionUnderLease(root);
    }

    internal async Task ResetSessionUnderLease(string root)
    {
        var active = seasons.EffectiveId(root);
        _sessions.TryRemove(active, out _);
        if (!seasons.IsSeasonal(active))
        {
            return;
        }
        var id = new MongoId(active);
        var original = saves.GetProfile(id);
        var seasonId = seasons.SeasonIdFor(original.CharacterData!.PmcData!);
        var definition = repository.Runtime(seasonId).Definition.Story;
        if (definition == null)
        {
            return;
        }
        var state = StoryStore.Read(original.CharacterData.PmcData!, seasonId);
        var initialized = false;
        foreach (var variable in definition.Variables.Where(v => v.Scope == StoryVariableScope.Profile))
        {
            initialized |= state.Variables.TryAdd(variable.Id, variable.InitialValue);
        }
        if (!initialized && state.Conversation == null && state.Raid is not { Finished: false })
        {
            return;
        }
        var staged = cloner.Clone(original)!;
        state.Conversation = null;
        if (state.Raid != null)
        {
            state.Raid.Finished = true;
            state.Raid.Pending.Clear();
            state.Raid.Cinematic = "";
        }
        state.Revision++;
        StoryStore.Write(staged.CharacterData!.PmcData!, state);
        RefreshMissionLinksUnderLease(id.ToString(), staged, seasonId);
        await commits.Commit(id, original, staged);
    }

    public async Task StartRaid(string sessionId, StartLocalRaidRequestData request, StartLocalRaidResponseData response)
    {
        if (!seasons.IsSeasonal(sessionId) || !string.Equals(request.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        using var lease = seasons.Enter(seasons.ResolveRoot(sessionId));
        var id = new MongoId(sessionId);
        var original = saves.GetProfile(id);
        var seasonId = seasons.SeasonIdFor(original.CharacterData!.PmcData!);
        var definition = repository.Runtime(seasonId).Definition.Story;
        if (definition == null || response.ServerId == null)
        {
            return;
        }
        var staged = cloner.Clone(original)!;
        var state = StoryStore.Read(staged.CharacterData!.PmcData!, seasonId);
        state.Conversation = null;
        state.Raid = new StoryRaid { Id = response.ServerId, Location = request.Location ?? "" };
        var collectibles = definition
            .RaidBindings.Where(b => b.Location == request.Location && b.Kind == "Collectible")
            .Select(b => b.ItemId)
            .ToHashSet();
        foreach (var item in response.LocationLoot?.Loot?.SelectMany(l => l.Items ?? []) ?? [])
        {
            if (collectibles.Contains(item.Template.ToString()))
            {
                state.Raid.SpawnedItems[item.Id.ToString()] = item.Template.ToString();
            }
        }
        state.Revision++;
        StoryStore.Write(staged.CharacterData.PmcData!, state);
        RefreshMissionLinksUnderLease(id.ToString(), staged, seasonId);
        await commits.Commit(id, original, staged);
    }

    private void ApplyRaid(StoryDefinition definition, StoryProgress state, StoryFacts facts, StoryEngine engine, StoryRequest request)
    {
        var raid = state.Raid;
        if (raid == null || raid.Finished || raid.Id != request.RaidId || !facts.InRaid)
        {
            throw new InvalidOperationException("This event belongs to a raid that is no longer active.");
        }
        var binding =
            definition.RaidBindings.SingleOrDefault(b => b.Id == request.Target && b.Location == raid.Location)
            ?? throw new InvalidOperationException("This story interaction is not registered for this location.");
        if (binding.Kind != "Collectible")
        {
            var separator = binding.ObjectPath.IndexOf(":/", StringComparison.Ordinal);
            var scene =
                binding.ZoneId.Length > 0 ? repository.Runtime(request.SeasonId).Definition.Zones.Single(z => z.Id == binding.ZoneId).Scene
                : separator < 0 ? ""
                : binding.ObjectPath[..separator];
            if (request.Scene != scene)
            {
                throw new InvalidOperationException("The interaction belongs to another scene.");
            }

            facts.Scene = scene;
        }
        if ((binding.Kind != "Cinematic" || request.Kind == "begin") && !StoryRules.Evaluate(binding.Condition, definition, state, facts))
        {
            throw new InvalidOperationException("The story interaction is not available yet.");
        }
        if (binding.Kind == "Cinematic")
        {
            if (request.Kind == "begin")
            {
                if (
                    raid.Cinematic.Length > 0
                    || binding.Once && (state.CompletedBindings.Contains(binding.Id) || raid.Seen.Contains(binding.Id))
                )
                {
                    return;
                }
                raid.Cinematic = binding.Id;
                engine.CinematicBindingId = binding.Id;
                engine.Presentation.Add(new StoryAction { Type = StoryActionType.StartCinematic, Target = binding.MediaId });
                return;
            }
            if (raid.Cinematic != binding.Id || request.Kind is not ("complete" or "skip" or "interrupt"))
            {
                throw new InvalidOperationException("No matching cinematic is active.");
            }
            raid.Cinematic = "";
            if (request.Kind == "interrupt")
            {
                return;
            }
        }
        else if (request.Kind != binding.Kind)
        {
            throw new InvalidOperationException("The event does not match its registered interaction.");
        }
        if (binding.Kind == "Collectible")
        {
            if (!raid.SpawnedItems.TryGetValue(request.ItemId, out var template) || template != binding.ItemId)
            {
                throw new InvalidOperationException("This collectible was not registered in the active raid's loot.");
            }
            if (!raid.PickedItems.Add(request.ItemId))
            {
                return;
            }
        }
        if (binding.Once && (raid.Seen.Contains(binding.Id) || state.CompletedBindings.Contains(binding.Id)))
        {
            return;
        }
        raid.Seen.Add(binding.Id);
        if (binding.Kind != "Cinematic")
        {
            engine.EventMediaId = binding.MediaId;
        }

        if (binding.PersistOnDeath)
        {
            CompleteBinding(binding, state, engine);
        }
        else
        {
            raid.Pending.Add(binding.Id);
        }
        if (binding.EntryPointId.Length > 0)
        {
            engine.Start(binding.EntryPointId, Guid.NewGuid().ToString("N"));
        }
    }

    private static void CompleteBinding(StoryRaidBinding binding, StoryProgress state, StoryEngine engine)
    {
        state.CompletedBindings.Add(binding.Id);
        if (binding.Kind == "Collectible")
        {
            state.CompletedItems.Add(binding.ItemId);
        }
        engine.Apply(binding.Actions);
    }

    public async Task FinishRaid(string sessionId, EndLocalRaidRequestData request)
    {
        if (!seasons.IsSeasonal(sessionId))
        {
            return;
        }
        // Called while the shared raid-end lease is held, after native raid profile reconciliation.
        var id = new MongoId(sessionId);
        var original = saves.GetProfile(id);
        var seasonId = seasons.SeasonIdFor(original.CharacterData!.PmcData!);
        var definition = repository.Runtime(seasonId).Definition.Story;
        if (definition == null)
        {
            return;
        }
        var staged = cloner.Clone(original)!;
        var pmc = staged.CharacterData!.PmcData!;
        var state = StoryStore.Read(pmc, seasonId);
        if (state.Raid == null || state.Raid.Finished || state.Raid.Id != request.ServerId)
        {
            return;
        }
        var facts = Facts(sessionId, staged, state, definition);
        MergeRaidNativeChanges(state.Raid, pmc);
        RefreshFacts(pmc, facts, definition, state);
        facts.InRaid = false;
        var notifications = new List<Func<Task>>();
        var engine = new StoryEngine(
            definition,
            state,
            facts,
            action =>
            {
                NativeAction(sessionId, pmc, definition, state, facts, action);
                RefreshFacts(pmc, facts, definition, state);
            },
            System.Security.Cryptography.RandomNumberGenerator.GetInt32,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        using (var scope = new StoryNativeScope(id, staged, cloner))
        {
            if (request.Results?.Result?.ToString().ToLowerInvariant() is "survived" or "runner" or "runthrough" or "transit")
            {
                foreach (var bindingId in state.Raid.Pending)
                {
                    CompleteBinding(definition.RaidBindings.Single(b => b.Id == bindingId), state, engine);
                }
            }
            state.Raid.Finished = true;
            state.Raid.Pending.Clear();
            state.Raid.Cinematic = "";
            state.Conversation = null;
            RefreshFacts(pmc, facts, definition, state);
            Reconcile(sessionId, pmc, definition, state, facts);
            if (scope.Output.Warnings?.Count > 0)
            {
                throw new InvalidOperationException("Native story reconciliation failed after the raid.");
            }
            notifications.AddRange(scope.Notifications);
        }
        state.Revision++;
        StoryStore.Write(pmc, state);
        RefreshMissionLinksUnderLease(id.ToString(), staged, seasonId);
        await commits.Commit(id, original, staged);
        foreach (var notification in notifications)
        {
            try
            {
                await notification();
            }
            catch { }
        }
    }
}
