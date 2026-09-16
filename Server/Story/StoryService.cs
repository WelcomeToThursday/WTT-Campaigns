using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

[Injectable(InjectionType.Singleton)]
public sealed partial class StoryService(
    SeasonService seasons,
    SeasonRepository repository,
    SaveServer saves,
    ICloner cloner,
    HubGameplay commits,
    QuestController quests,
    SPTarkov.Server.Core.Helpers.Traders.TraderHelper traderHelper,
    JsonUtil json,
    TemplateTable templates
)
{
    private readonly ConcurrentDictionary<string, Dictionary<string, int>> _sessions = new();
    private readonly ConcurrentDictionary<string, StoryPreparation> _preparations = new();
    private readonly ConcurrentDictionary<string, (string Raid, long Sequence)> _observations = new();

    private (string Id, SptProfile Profile, StoryDefinition Definition) Active(string sessionId, StoryRequest request)
    {
        var id = seasons.EffectiveId(seasons.ResolveRoot(sessionId));
        if (request.Version is not (1 or 2) || request.CharacterId != id || !seasons.IsSeasonal(id))
        {
            throw new InvalidOperationException("Refresh the active Campaign character before using its story.");
        }
        var profile = saves.GetProfile(new MongoId(id));
        var seasonId = seasons.SeasonIdFor(profile.CharacterData!.PmcData!);
        if (seasonId != request.SeasonId || !repository.Playable.TryGetValue(seasonId, out var runtime))
        {
            throw new InvalidOperationException("This request belongs to another campaign.");
        }
        // The journal shell and trader visits are systems available without authored content.
        // Keep this fallback transient so existing season identities and definitions stay unchanged.
        return (id, profile, runtime.Definition.Story ?? new StoryDefinition());
    }

    public StoryResponse Read(string sessionId, StoryRequest request)
    {
        using var lease = seasons.Enter(seasons.ResolveRoot(sessionId));
        var active = Active(sessionId, request);
        if (request.Version != 2)
        {
            throw new InvalidOperationException("Update both WTT-Campaigns client and server to story protocol 2.");
        }

        ValidateObservation(active.Id, active.Profile, request);
        return Snapshot(active.Id, active.Profile, active.Definition, request.SeasonId, request);
    }

    private StoryResponse Snapshot(string id, SptProfile profile, StoryDefinition definition, string seasonId, StoryRequest? request = null)
    {
        var state = StoryStore.Read(profile.CharacterData!.PmcData!, seasonId);
        var facts = Facts(id, profile, state, definition, request);
        // Receipts remain durable on the authority; the client only needs the current projection.
        state.Receipts.Clear();
        return new StoryResponse
        {
            CharacterId = id,
            SeasonId = seasonId,
            Revision = state.Revision,
            Definition = definition,
            State = state,
            Facts = facts,
            Choices = StoryRules.EligibleLines(definition, state, facts).Where(l => l.Side == "Player").ToList(),
            Line = definition.Dialogs.SelectMany(d => d.Lines).FirstOrDefault(l => l.Id == state.Conversation?.CurrentLineId),
            Objectives = Objectives(profile.CharacterData.PmcData!, definition, state, facts),
            RaidConditionIds = repository
                .Runtime(seasonId)
                .Definition.Quests.SelectMany(q => q.AllConditions())
                .Select(c => c.Id)
                .Distinct()
                .ToList(),
        };
    }

    public async Task<StoryResponse> Transact(string sessionId, StoryRequest request, string operation, bool prepare = false)
    {
        using var lease = seasons.Enter(seasons.ResolveRoot(sessionId));
        var active = Active(sessionId, request);
        if (
            request.ItemIds.Count > 256
            || request.ItemIds.Distinct().Count() != request.ItemIds.Count
            || request.ItemIds.Any(i => !Shared.Seasons.SeasonValidator.IsId(i))
        )
        {
            throw new InvalidOperationException("The handover selection is invalid.");
        }
        if (!Guid.TryParseExact(request.OperationId, "N", out _))
        {
            throw new InvalidOperationException("A unique operation identifier is required.");
        }
        var fingerprint = RequestFingerprint(operation, request);
        var state = StoryStore.Read(active.Profile.CharacterData!.PmcData!, request.SeasonId);
        if (state.Receipts.TryGetValue(request.OperationId, out var receipt))
        {
            if (receipt.RequestHash != fingerprint)
            {
                throw new InvalidOperationException("This operation identifier was already used for different inputs.");
            }
            var replay = Snapshot(active.Id, active.Profile, active.Definition, request.SeasonId);
            replay.Replayed = true;
            replay.NativeUpdate = receipt.NativeUpdate;
            replay.NativeRevision = receipt.Revision;
            replay.NativeProfileChanged = receipt.NativeUpdate.Length > 0;
            // Presentation effects are not replayed: they may open screens or start cinematics.
            return replay;
        }
        if (request.Version != 2)
        {
            throw new InvalidOperationException("This legacy operation was not committed. Refresh and retry with the updated client.");
        }

        ValidateObservation(active.Id, active.Profile, request);
        if (state.Revision != request.ExpectedRevision)
        {
            throw new InvalidOperationException("Story progress changed. Refresh and try again.");
        }
        foreach (var variable in active.Definition.Variables.Where(v => v.Scope == StoryVariableScope.Profile))
        {
            state.Variables.TryAdd(variable.Id, variable.InitialValue);
        }
        var staged = cloner.Clone(active.Profile)!;
        var facts = Facts(active.Id, staged, state, active.Definition, request);
        var preparation = GetPreparation(active.Id, active.Profile, request, operation, prepare, facts);
        var drawIndex = 0;
        var sessionVariables = new Dictionary<string, int>(facts.SessionVariables);
        facts.SessionVariables = sessionVariables;
        var notifications = new List<Func<Task>>();
        var nativeChanged = false;
        var nativeUpdate = "";
        var nativeBefore = json.Serialize(staged.CharacterData!.PmcData);
        var engine = new StoryEngine(
            active.Definition,
            state,
            facts,
            action =>
            {
                var selected = request.Selections.GetValueOrDefault(action.Id) ?? (IReadOnlyCollection<string>)request.ItemIds;
                if (action.Type == StoryActionType.HandoverItem && preparation != null)
                {
                    selected = PrepareHandover(
                        preparation,
                        prepare,
                        request,
                        staged.CharacterData!.PmcData!,
                        state,
                        active.Definition,
                        facts,
                        action
                    );
                }

                NativeAction(active.Id, staged.CharacterData!.PmcData!, active.Definition, state, facts, action, selected);
                nativeChanged = true;
                RefreshFacts(staged.CharacterData.PmcData!, facts, active.Definition, state);
            },
            maximum => preparation?.Draw(drawIndex++, maximum) ?? RandomNumberGenerator.GetInt32(maximum),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        using (var scope = new StoryNativeScope(new MongoId(active.Id), staged, cloner))
        {
            try
            {
                switch (operation)
                {
                    case "start":
                        ValidateEntryLocation(active.Definition, facts, request);
                        engine.Start(request.Target, Guid.NewGuid().ToString("N"));
                        break;
                    case "select":
                        engine.Select(request.ConversationId, request.Target);
                        break;
                    case "close":
                        engine.Close(request.ConversationId);
                        break;
                    case "read":
                        MarkRead(
                            active.Definition,
                            state,
                            request,
                            Objectives(staged.CharacterData!.PmcData!, active.Definition, state, facts)
                        );
                        break;
                    case "reconcile":
                        break;
                    case "raid":
                        ApplyRaid(active.Definition, state, facts, engine, request);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown story operation.");
                }
                Reconcile(active.Id, staged.CharacterData!.PmcData!, active.Definition, state, facts);
            }
            catch (StoryHandoverRequired required) when (prepare && preparation != null)
            {
                preparation.Pending = required.Handover;
                preparation.Ready = false;
                var selection = Snapshot(active.Id, active.Profile, active.Definition, request.SeasonId, request);
                selection.PreparationId = preparation.Id;
                selection.Handover = required.Handover;
                return selection;
            }
            if (scope.Output.Warnings?.Count > 0)
            {
                throw new InvalidOperationException("The native quest operation was rejected; story progress was not changed.");
            }
            notifications.AddRange(scope.Notifications);
            var pmc = staged.CharacterData!.PmcData!;
            var change = scope.Output.ProfileChanges![new MongoId(active.Id)];
            change.Experience = pmc.Info!.Experience;
            change.QuestsStatus = pmc.Quests;
            change.Skills = cloner.Clone(pmc.Skills);
            nativeChanged |= json.Serialize(pmc) != nativeBefore;
            nativeUpdate = nativeChanged ? json.Serialize(change)! : "";
        }
        if (prepare && preparation != null)
        {
            preparation.Ready = true;
            preparation.Pending = null;
            var ready = Snapshot(active.Id, active.Profile, active.Definition, request.SeasonId, request);
            ready.PreparationId = preparation.Id;
            return ready;
        }
        state.Revision++;
        if (state.Raid is { Finished: false } raid)
        {
            RecordRaidNativeChanges(raid, active.Profile.CharacterData!.PmcData!, staged.CharacterData!.PmcData!);
        }
        state.Receipts.Add(
            request.OperationId,
            new StoryReceipt
            {
                RequestHash = fingerprint,
                Revision = state.Revision,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LineId = state.Conversation?.CurrentLineId ?? "",
                NativeUpdate = nativeUpdate,
            }
        );
        StoryStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), active.Profile, staged);
        if (preparation != null)
        {
            _preparations.TryRemove(preparation.Id, out _);
        }

        _sessions[active.Id] = sessionVariables;
        foreach (var notification in notifications)
        {
            // Mail is already durable. A failed notification must not make a committed choice appear to have failed.
            try
            {
                await notification();
            }
            catch { }
        }
        var result = Snapshot(active.Id, staged, active.Definition, request.SeasonId, request);
        result.Presentation = engine.Presentation;
        result.Lines = engine.Lines;
        result.NativeProfileChanged = nativeChanged;
        result.NativeUpdate = nativeUpdate;
        result.NativeRevision = state.Revision;
        result.EventMediaId = engine.EventMediaId;
        result.CinematicBindingId = engine.CinematicBindingId;
        return result;
    }

    private static void MarkRead(StoryDefinition definition, StoryProgress state, StoryRequest request, List<StoryObjective> objectives)
    {
        switch (request.Kind)
        {
            case "note" when state.Notes.ContainsKey(request.Target):
                state.ReadNotes.Add(request.Target);
                break;
            case "link"
                when definition.Notes.Where(n => state.Notes.ContainsKey(n.Id)).SelectMany(n => n.Links).Any(l => l.Id == request.Target):
                state.ReadLinks.Add(request.Target);
                break;
            case "condition"
                when objectives.Any(o => o.Id == request.Target && o.Visible)
                    && definition.Chapters.Any(c => c.Id == objectives.Single(o => o.Id == request.Target).ChapterId):
                state.ReadConditions.Add(request.Target);
                break;
            default:
                throw new InvalidOperationException("Only discovered story entries can be marked as read.");
        }
    }

    private static void ValidateEntryLocation(StoryDefinition definition, StoryFacts facts, StoryRequest request)
    {
        var entry =
            definition.EntryPoints.SingleOrDefault(e => e.Id == request.Target)
            ?? throw new InvalidOperationException("Unknown story entry point.");
        if (entry.Kind != "InLobby")
        {
            throw new InvalidOperationException("This entry point must be activated by its raid interaction.");
        }
        if (facts.InRaid)
        {
            throw new InvalidOperationException("Trader visits are not available during a raid.");
        }
    }
}
