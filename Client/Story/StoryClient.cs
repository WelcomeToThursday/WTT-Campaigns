using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Story;
using SPT.Common.Http;

namespace SeasonalPerks.Client.Story;

internal static class StoryClient
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static string _character = "";
    private static long _appliedRevision = -1;
    private static object? _session;
    internal static StoryResponse? Current { get; private set; }
    internal static event Action? Changed;

    internal static bool Available
    {
        get { return Plugin.Current?.ActiveMode == "seasonal" && Plugin.App?.Session?.Profile?.Id == Plugin.Current.EffectiveProfileId; }
    }

    internal static void Reset()
    {
        _character = "";
        _appliedRevision = -1;
        Current = null;
        _session = null;
    }

    private static StoryRequest Request()
    {
        if (!Available)
        {
            throw new InvalidOperationException("Select a Seasonal character with a story first.");
        }
        return new StoryRequest
        {
            CharacterId = Plugin.Current!.EffectiveProfileId,
            SeasonId = Plugin.Current.SeasonId,
            ExpectedRevision = Current?.Revision ?? 0,
            OperationId = Guid.NewGuid().ToString("N"),
            ConversationId = Current?.State?.Conversation?.Id ?? "",
        };
    }

    internal static async Task<StoryResponse> Load()
    {
        await Gate.WaitAsync();
        try
        {
            var request = Request();
            var fresh = _character != request.CharacterId || !ReferenceEquals(_session, Plugin.App!.Session);
            if (fresh)
            {
                Reset();
                _character = request.CharacterId;
                _session = Plugin.App!.Session;
            }
            var state = await Send("", request);
            if (fresh)
            {
                // On login the native profile already includes every committed receipt.
                _appliedRevision = state.Revision;
            }
            Accept(state);
            return state;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task<StoryResponse> Mutate(
        string operation,
        string target = "",
        string kind = "",
        IEnumerable<string>? itemIds = null,
        string itemId = ""
    )
    {
        await Gate.WaitAsync();
        var priorBusy = Plugin.Busy;
        try
        {
            Plugin.Busy = true;
            var request = Request();
            var pendingPath = Path.Combine(Plugin.Folder, "story-pending-" + request.CharacterId + ".json");
            if (File.Exists(pendingPath))
            {
                var pending =
                    JsonConvert.DeserializeObject<PendingStoryOperation>(File.ReadAllText(pendingPath))
                    ?? throw new InvalidDataException("Invalid pending story operation.");
                var previous = pending.Request;
                var previousOperation = pending.Operation;
                if (
                    previous.CharacterId != request.CharacterId
                    || previous.SeasonId != request.SeasonId
                    || !new[] { "start", "select", "close", "read", "reconcile", "raid" }.Contains(previousOperation)
                )
                {
                    throw new InvalidDataException("The pending story operation is invalid.");
                }
                var replay = await Send(previousOperation, previous, () => File.Delete(pendingPath));
                Accept(replay);
                File.Delete(pendingPath);
                if (
                    previousOperation == operation
                    && previous.Target == target
                    && previous.Kind == kind
                    && previous.ItemId == itemId
                    && previous.ItemIds.ToHashSet().SetEquals(itemIds ?? Enumerable.Empty<string>())
                )
                {
                    return replay;
                }
                request = Request();
            }
            if (!Plugin.InRaid)
            {
                await Plugin.FlushPendingOperations();
            }
            request.Target = target;
            request.ItemIds = itemIds?.ToList() ?? new();
            request.ItemId = itemId;
            request.Kind = kind;
            request.RaidId = Current?.State?.Raid?.Id ?? "";
            var payload = JsonConvert.SerializeObject(new PendingStoryOperation { Operation = operation, Request = request });
            File.WriteAllText(pendingPath + ".tmp", payload);
            if (File.Exists(pendingPath))
            {
                File.Replace(pendingPath + ".tmp", pendingPath, null);
            }
            else
            {
                File.Move(pendingPath + ".tmp", pendingPath);
            }
            var response = await Send(operation, request, () => File.Delete(pendingPath));
            Accept(response);
            File.Delete(pendingPath);
            return response;
        }
        finally
        {
            Plugin.Busy = priorBusy;
            Gate.Release();
        }
    }

    private static async Task<StoryResponse> Send(string operation, StoryRequest request, Action? rejected = null)
    {
        var body = await RequestHandler.PostJsonAsync(
            "/wtt-seasonal/story" + (operation.Length > 0 ? "/" + operation : ""),
            JsonConvert.SerializeObject(request)
        );
        var response = JsonConvert.DeserializeObject<StoryResponse>(body) ?? throw new InvalidDataException("Empty story response.");
        if (response.Error != null)
        {
            rejected?.Invoke();
            throw new InvalidOperationException(response.Error);
        }
        if (
            response.Version != 1
            || response.CharacterId != request.CharacterId
            || response.SeasonId != request.SeasonId
            || Plugin.App?.Session?.Profile?.Id != request.CharacterId
        )
        {
            throw new InvalidOperationException("The character changed while loading its story.");
        }
        return response;
    }

    private static void Accept(StoryResponse response)
    {
        var profile = Plugin.InRaid && Plugin.SeasonalPlayer ? Plugin.Player!.Profile : Plugin.App!.Session.Profile;
        var definition = response.Definition!;
        var state = response.State!;
        foreach (var variable in definition.Variables.Where(v => v.Scope == StoryVariableScope.Profile))
        {
            profile.ProfileVariables.SetVariableValue(variable.Id, state.Variables.GetValueOrDefault(variable.Id, variable.InitialValue));
        }
        foreach (var binding in definition.RaidBindings)
        {
            profile.ProfileVariables.SetVariableValue(binding.Id, state.CompletedBindings.Contains(binding.Id) ? 1 : 0);
        }
        foreach (var itemId in definition.RaidBindings.Where(b => b.ItemId.Length > 0).Select(b => b.ItemId).Distinct())
        {
            profile.ProfileVariables.SetVariableValue(itemId, state.CompletedItems.Contains(itemId) ? 1 : 0);
        }
        if (response.NativeUpdate.Length > 0 && response.NativeRevision > _appliedRevision)
        {
            var changes = JsonConvert.DeserializeObject<ProfileChanges>(response.NativeUpdate, EftJsonConverters.Converters)!;
            if (Plugin.InRaid && Plugin.SeasonalPlayer)
            {
                // Native rewards are merged by the server at raid end. Do not replace earned raid XP/skills
                // or local counters with the lobby's older snapshot.
                var owned = definition.Quests.Select(q => q.QuestId).ToHashSet();
                Plugin.Player!.QuestController.Quests.SetQuestStatusData(changes.QuestsStatus.Where(q => owned.Contains(q.Id)).ToArray());
            }
            else
            {
                var updaters =
                    AccessTools.Field(typeof(ClientBackendSession), "_profileUpdaters").GetValue(Plugin.App!.Session)
                    as Dictionary<string, IProfileUpdatesHandler>;
                if (updaters?.TryGetValue(profile.Id, out var updater) != true)
                {
                    throw new InvalidOperationException(
                        "The native profile updater is not ready. Reopen the story to reconcile this choice."
                    );
                }
                updater.UpdateProfile(changes);
            }
            _appliedRevision = response.NativeRevision;
        }
        foreach (var counter in response.Facts!.ConditionCounters.Where(_ => !Plugin.InRaid))
        {
            if (profile.TaskConditionCounters.TryGetValue(counter.Key, out var native))
            {
                native.Value = checked((int)counter.Value);
            }
        }
        Current = response;
        _character = response.CharacterId;
        Changed?.Invoke();
    }
}
