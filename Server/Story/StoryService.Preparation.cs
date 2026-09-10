using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string RequestFingerprint(string operation, StoryRequest request)
    {
        var token = JObject.FromObject(request);
        if (request.Version == 1)
        {
            foreach (var name in new[] { "Scene", "Operation", "PreparationId", "Selections", "Observation" })
            {
                token.Remove(name);
            }
        }

        return Hash(new JObject { ["operation"] = operation, ["request"] = token }.ToString(Formatting.None));
    }

    private StoryPreparation? GetPreparation(
        string id,
        SptProfile profile,
        StoryRequest request,
        string operation,
        bool prepare,
        StoryFacts facts
    )
    {
        if (!prepare && request.PreparationId.Length == 0)
        {
            return null;
        }

        if (operation is not ("start" or "select") || facts.InRaid)
        {
            throw new InvalidOperationException("Only lobby conversations can prepare item selections.");
        }

        foreach (var expired in _preparations.Where(p => p.Value.Expires < DateTime.UtcNow).Select(p => p.Key))
        {
            _preparations.TryRemove(expired, out _);
        }

        var identity = Hash(
            JsonConvert.SerializeObject(
                new
                {
                    id,
                    operation,
                    request.OperationId,
                    request.SeasonId,
                    request.CharacterId,
                    request.ExpectedRevision,
                    request.ConversationId,
                    request.Target,
                    request.Scene,
                }
            )
        );
        var profileHash = Hash(json.Serialize(profile)! + JsonConvert.SerializeObject(facts.SessionVariables));
        StoryPreparation preparation;
        if (request.PreparationId.Length == 0)
        {
            if (!prepare || request.Selections.Count > 0 || _preparations.Count >= 256)
            {
                throw new InvalidOperationException("Refresh the conversation before selecting items.");
            }

            preparation =
                _preparations.Values.FirstOrDefault(p => p.Identity == identity)
                ?? new() { Identity = identity, ProfileHash = profileHash };
            _preparations[preparation.Id] = preparation;
        }
        else if (!_preparations.TryGetValue(request.PreparationId, out preparation!))
        {
            throw new InvalidOperationException("The item selection expired. Reopen the conversation.");
        }

        if (preparation.Identity != identity || preparation.ProfileHash != profileHash)
        {
            throw new InvalidOperationException("The character, inventory, or story changed. Reopen the conversation.");
        }

        if (
            request.Selections.Count > 256
            || request.Selections.Any(s => s.Value.Count > 256 || s.Value.Distinct().Count() != s.Value.Count)
        )
        {
            throw new InvalidOperationException("Invalid handover selections.");
        }

        foreach (var selection in request.Selections)
        {
            if (preparation.Selections.TryGetValue(selection.Key, out var prior))
            {
                if (!prior.ToHashSet().SetEquals(selection.Value))
                {
                    throw new InvalidOperationException("A prepared item selection changed.");
                }
            }
            else if (
                prepare
                && preparation.Pending?.ActionId == selection.Key
                && selection.Value.Count > 0
                && selection.Value.All(preparation.Pending.Candidates.Contains)
            )
            {
                preparation.Selections[selection.Key] = selection.Value.ToList();
            }
            else
            {
                throw new InvalidOperationException("The selected items do not belong to the pending handover.");
            }
        }
        if (!prepare && (!preparation.Ready || preparation.Selections.Count != request.Selections.Count))
        {
            throw new InvalidOperationException("Finish choosing items before committing the conversation.");
        }

        return preparation;
    }

    private IReadOnlyCollection<string> PrepareHandover(
        StoryPreparation preparation,
        bool prepare,
        StoryRequest request,
        PmcData pmc,
        StoryProgress state,
        StoryDefinition definition,
        StoryFacts facts,
        StoryAction action
    )
    {
        var questId = action.QuestId.Length > 0 ? action.QuestId : state.Conversation?.SelectedQuestId ?? "";
        if (facts.InRaid || facts.QuestStatuses.GetValueOrDefault(questId) is not ("Started" or "AvailableForFinish"))
        {
            throw new InvalidOperationException("Quest items can only be handed over for an active quest outside a raid.");
        }

        var condition = QuestTemplate(state, definition, questId)
            .Conditions.AvailableForFinish.Single(c => c.Id == action.ConditionId && c.ConditionType == "HandoverItem");
        var candidates = HandoverItems(pmc, condition).ToArray();
        if (preparation.Selections.TryGetValue(action.Id, out var selection))
        {
            if (!selection.All(id => candidates.Any(i => i.Id.ToString() == id)))
            {
                throw new InvalidOperationException("An item is no longer available for this handover.");
            }

            return selection;
        }
        if (candidates.Any(i => !AutomaticHandover(i)))
        {
            if (!prepare)
            {
                throw new InvalidOperationException("This handover requires an item selection.");
            }

            throw new StoryHandoverRequired(
                new StoryHandover
                {
                    ActionId = action.Id,
                    QuestId = questId,
                    ConditionId = action.ConditionId,
                    ConditionJson = JsonConvert.SerializeObject(condition),
                    Current = facts.ConditionCounters.GetValueOrDefault(action.ConditionId),
                    Candidates = candidates.Select(i => i.Id.ToString()).ToList(),
                }
            );
        }
        return Array.Empty<string>();
    }
}
