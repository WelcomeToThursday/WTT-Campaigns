using Newtonsoft.Json;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Seasons;

public static class SeasonCompiler
{
    public static T Copy<T>(T value)
    {
        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    }

    public static Dictionary<string, string> Texts(SeasonDefinition season, string language = "en")
    {
        var texts = new Dictionary<string, string>(season.Locales.GetValueOrDefault("en") ?? new());
        Story.StoryContent.AddTexts(season.Story, texts);
        texts[season.Id + " name"] = season.Name;
        texts[season.Id + " description"] = season.Description;
        foreach (var item in season.Items)
        {
            texts[item.Id + " Name"] = item.Name;
            texts[item.Id + " ShortName"] = item.Name;
            texts[item.Id + " Description"] = item.Description;
        }
        foreach (var doc in season.Documents)
        {
            texts[doc.Id + " name"] = doc.Name;
        }

        foreach (var reward in season.AllRewards)
        {
            texts[reward.Id + " name"] = reward.Name;
            texts[reward.Id + " description"] = reward.Description;
        }
        for (var i = 0; i < season.Slides.Count; i++)
        {
            texts[season.Id + " slide " + i + " text"] = season.Slides[i].Text;
        }
        foreach (var quest in season.Quests)
        {
            foreach (var pair in quest.Localization.GetValueOrDefault("en") ?? new())
            {
                texts[pair.Key] = (string?)pair.Value ?? "";
            }
        }

        if (language != "en")
        {
            foreach (var quest in season.Quests)
            {
                foreach (var pair in quest.Localization.GetValueOrDefault(language) ?? new())
                {
                    if (!string.IsNullOrEmpty((string?)pair.Value))
                    {
                        texts[pair.Key] = (string)pair.Value!;
                    }
                }
            }

            foreach (var pair in season.Locales.GetValueOrDefault(language) ?? new())
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    texts[pair.Key] = pair.Value;
                }
            }
        }
        return texts;
    }

    public static IEnumerable<string> Dependencies(SeasonDefinition season)
    {
        var owned = season.Items.Select(i => i.Id).Concat(season.ImportedItems.Keys).ToHashSet();
        var items = season
            .Items.Select(i => i.CloneFrom)
            .Concat(season.Documents.Select(d => d.ItemId))
            .Concat(season.Crates.SelectMany(c => c.Pool.Keys))
            .Concat(new[] { season.Starting.Usec, season.Starting.Bear }.SelectMany(f => f.Items.Select(i => i.Template)))
            .Concat(
                season
                    .AllRewards.SelectMany(r => r.Grants)
                    .SelectMany(g => g.Items)
                    .Concat(season.Quests.SelectMany(q => q.AllItems()))
                    .Select(i => i.Template)
            );
        return season
            .Dependencies.Concat(items.Where(i => !owned.Contains(i)).Select(i => "item:" + i))
            .Distinct()
            .OrderBy(i => i, StringComparer.Ordinal);
    }

    public static HubState Hub(SeasonDefinition season)
    {
        return new()
        {
            Id = season.BattlePassId,
            SeasonId = season.Id,
            SeasonName = season.Name,
            PackRevision = season.Revision,
            BadgeImage = season.Branding.Badge,
            BannerImage = season.Branding.Banner,
            LegacyBranding = season.Legacy,
            WindowSeconds = season.Collection.WindowSeconds,
            DocumentLimit = season.Collection.DocumentLimit,
            Documents = season
                .Documents.Select(d => new HubDocument
                {
                    Id = d.Id,
                    Name = d.Name,
                    Image = d.Image,
                    UnavailableImage = d.UnavailableImage,
                    ItemId = d.ItemId,
                })
                .ToArray(),
            Pages = season
                .Pages.Select(p => new HubPage
                {
                    PreviousRequirement = p.PreviousRequirement,
                    Rewards = p.Rewards.Where(r => r.Enabled).Select(Tile).ToArray(),
                })
                .ToArray(),
            SeasonalRewards = season.SeasonalRewards.Where(r => r.Enabled).Select(Tile).ToArray(),
            Slides = Copy(season.Slides).ToArray(),
            UniversalImage = season.UniversalImage,
            UniversalUnavailableImage = season.UniversalUnavailableImage,
        };
    }

    private static HubReward Tile(SeasonReward reward)
    {
        return JsonConvert.DeserializeObject<HubReward>(JsonConvert.SerializeObject(reward))!;
    }

    public static HubGameplayDefinition Gameplay(SeasonDefinition season)
    {
        return new()
        {
            Id = season.BattlePassId,
            SeasonId = season.Id,
            ExchangeRate = season.ExchangeRate,
            ItemExchange = new() { ItemId = season.ExchangeCrate, RequiredDocuments = season.CrateCost },
            Documents = season.Documents.Select(d => new HubGameplayDocument { Id = d.Id, ItemId = d.ItemId }).ToList(),
            Rewards = season
                .AllRewards.Where(r => r.Enabled)
                .ToDictionary(r => r.Id, r => new HubGameplayReward { Grants = Copy(r.Grants), Conditions = Copy(r.Conditions) }),
            Offers = Copy(season.Offers),
        };
    }

    public static IEnumerable<string> Assets(SeasonDefinition season)
    {
        return season
            .Perks.All.Select(p => p.ImageUrl)
            .Concat(season.Documents.SelectMany(d => new[] { d.Image, d.UnavailableImage }))
            .Concat(season.AllRewards.SelectMany(r => new[] { r.Image, r.BigImage }))
            .Concat(new[] { season.UniversalImage, season.UniversalUnavailableImage, season.Branding.Badge, season.Branding.Banner })
            .Concat(season.Slides.Select(s => s.Image))
            .Concat(season.Story?.Chapters.SelectMany(c => new[] { c.Image, c.Icon }) ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct();
    }

    public static string GameplayIdentity(SeasonDefinition season)
    {
        return GameplaySerialization.Identity(season);
    }
}
