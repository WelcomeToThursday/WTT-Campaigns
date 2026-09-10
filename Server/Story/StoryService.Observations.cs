using SPTarkov.Server.Core.Models.Eft.Profile;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private void ValidateObservation(string id, SptProfile profile, StoryRequest request)
    {
        if (request.Scene.Length > 256)
        {
            throw new InvalidOperationException("Invalid scene name.");
        }

        if (request.Observation is not { } observation)
        {
            return;
        }

        var state = StoryStore.Read(profile.CharacterData!.PmcData!, request.SeasonId);
        var season = repository.Runtime(request.SeasonId).Definition;
        var conditions = season.Quests.SelectMany(q => q.AllConditions()).Select(c => c.Id).ToHashSet();
        StoryObservationRules.Validate(observation, id, state.Raid, conditions);
        var skills = (profile.CharacterData.PmcData!.Skills?.Common ?? []).Select(s => s.Id.ToString()).ToHashSet();
        if (observation.Skills.Keys.Any(s => !skills.Contains(s)))
        {
            throw new InvalidOperationException("Unknown raid skill.");
        }
        foreach (var item in observation.Items)
        {
            _ = ObservedItem(item);
        }

        if (observation.Items.Any(i => !templates.Items.ContainsKey(new(i.Template!))))
        {
            throw new InvalidOperationException("Unknown raid item template.");
        }

        if (
            _observations.TryGetValue(id, out var previous)
            && previous.Raid == observation.RaidId
            && previous.Sequence >= observation.Sequence
        )
        {
            throw new InvalidOperationException("The raid observation is stale. Refresh and retry.");
        }

        _observations[id] = (observation.RaidId, observation.Sequence);
    }

    private SPTarkov.Server.Core.Models.Eft.Common.Tables.Item ObservedItem(StoryObservedItem item)
    {
        if (item.Data.Length == 0)
        {
            return new()
            {
                Id = new(item.Id),
                Template = new(item.Template),
                Upd = new() { StackObjectsCount = item.StackCount },
            };
        }

        try
        {
            var token = Newtonsoft.Json.Linq.JObject.Parse(item.Data);
            if (
                token
                    .Descendants()
                    .OfType<Newtonsoft.Json.Linq.JValue>()
                    .Any(v =>
                        v.Type == Newtonsoft.Json.Linq.JTokenType.Float
                        && (!double.IsFinite(Convert.ToDouble(v.Value)) || Math.Abs(Convert.ToDouble(v.Value)) > 1e9)
                    )
            )
            {
                throw new InvalidOperationException("Invalid numeric raid item data.");
            }

            var native = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item>(item.Data)!;
            if (
                native.Id.ToString() != item.Id
                || native.Template.ToString() != item.Template
                || (native.Upd?.StackObjectsCount ?? 1) != item.StackCount
            )
            {
                throw new InvalidOperationException("Inconsistent raid item data.");
            }

            return native;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            throw new InvalidOperationException("Invalid raid item data.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("Invalid raid item data.");
        }
    }
}
