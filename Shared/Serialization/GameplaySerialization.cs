using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Perks;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Shared.Serialization;

// Presentation exclusions use concrete contracts. JSON tokens only canonicalize the wire format.
internal static class GameplaySerialization
{
    private static readonly GameplayContractResolver Resolver = new();

    public static string Identity(SeasonDefinition season)
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = Resolver });
        return Canonical(JToken.FromObject(season, serializer)).ToString(Formatting.None);
    }

    private static JToken Canonical(JToken token)
    {
        return token switch
        {
            JObject obj => new JObject(
                obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value)))
            ),
            JArray array => new JArray(array.Select(Canonical)),
            _ => token.DeepClone(),
        };
    }

    private sealed class GameplayContractResolver : DefaultContractResolver
    {
        private static readonly Dictionary<Type, HashSet<string>> Presentation = new()
        {
            [typeof(SeasonDefinition)] = new()
            {
                nameof(SeasonDefinition.Name),
                nameof(SeasonDefinition.Description),
                nameof(SeasonDefinition.Author),
                nameof(SeasonDefinition.Version),
                nameof(SeasonDefinition.Revision),
                nameof(SeasonDefinition.Branding),
                nameof(SeasonDefinition.Locales),
                nameof(SeasonDefinition.Slides),
                nameof(SeasonDefinition.UniversalImage),
                nameof(SeasonDefinition.UniversalUnavailableImage),
            },
            [typeof(StoryChapter)] = new() { nameof(StoryChapter.Name), nameof(StoryChapter.Image), nameof(StoryChapter.Icon) },
            [typeof(CampaignTraderOffer)] = new() { nameof(CampaignTraderOffer.Name) },
            [typeof(StoryNote)] = new() { nameof(StoryNote.Text) },
            [typeof(StoryDialogLine)] = new()
            {
                nameof(StoryDialogLine.Text),
                nameof(StoryDialogLine.Icon),
                nameof(StoryDialogLine.Confirmation),
                nameof(StoryDialogLine.Playback),
            },
            [typeof(Perk)] = new() { nameof(Perk.ImageUrl) },
            [typeof(SeasonDocument)] = new()
            {
                nameof(SeasonDocument.Name),
                nameof(SeasonDocument.Image),
                nameof(SeasonDocument.UnavailableImage),
            },
            [typeof(SeasonReward)] = new()
            {
                nameof(SeasonReward.Name),
                nameof(SeasonReward.Description),
                nameof(SeasonReward.Image),
                nameof(SeasonReward.BigImage),
                nameof(SeasonReward.Kind),
                nameof(SeasonReward.Requirements),
            },
            [typeof(SeasonItem)] = new() { nameof(SeasonItem.Name), nameof(SeasonItem.Description) },
            [typeof(NativeQuest)] = new()
            {
                nameof(NativeQuest.Localization),
                nameof(NativeQuest.QuestName),
                nameof(NativeQuest.Name),
                nameof(NativeQuest.Description),
                nameof(NativeQuest.Image),
                nameof(NativeQuest.StartedMessageText),
                nameof(NativeQuest.SuccessMessageText),
                nameof(NativeQuest.FailMessageText),
                nameof(NativeQuest.AcceptPlayerMessage),
                nameof(NativeQuest.CompletePlayerMessage),
                nameof(NativeQuest.DeclinePlayerMessage),
            },
        };

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (
                member.DeclaringType != null
                && Presentation.TryGetValue(member.DeclaringType, out var fields)
                && fields.Contains(member.Name)
            )
            {
                property.Ignored = true;
            }
            return property;
        }
    }
}
