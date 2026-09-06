using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Contracts;

namespace SeasonalPerks.Shared.Seasons;

public static class SeasonCompiler
{
    public static T Copy<T>(T value)
    {
        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    }

    public static Dictionary<string, string> Texts(SeasonDefinition season, string language = "en")
    {
        var texts = new Dictionary<string, string>(season.Locales.GetValueOrDefault("en") ?? new());
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
        foreach (var quest in season.Quests.OfType<JObject>())
        {
            foreach (var pair in (quest["localization"]?["en"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                texts[pair.Name] = (string?)pair.Value ?? "";
            }
        }

        if (language != "en")
        {
            foreach (var quest in season.Quests.OfType<JObject>())
            {
                foreach (var pair in (quest["localization"]?[language] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    if (!string.IsNullOrEmpty((string?)pair.Value))
                    {
                        texts[pair.Name] = (string)pair.Value!;
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
        var owned = season.Items.Select(i => i.Id).Concat(season.ImportedItems.Properties().Select(p => p.Name)).ToHashSet();
        var items = season
            .Items.Select(i => i.CloneFrom)
            .Concat(season.Documents.Select(d => d.ItemId))
            .Concat(season.Crates.SelectMany(c => c.Pool.Keys))
            .Concat(new[] { season.Starting.Usec, season.Starting.Bear }.SelectMany(f => f.Items.Select(i => i.Template)))
            .Concat(
                season
                    .AllRewards.SelectMany(r => r.Grants)
                    .Concat(season.Quests)
                    .OfType<JObject>()
                    .SelectMany(o => o.Descendants().OfType<JProperty>())
                    .Where(p => p.Name == "_tpl")
                    .Select(p => (string)p.Value!)
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

    public static JObject Gameplay(SeasonDefinition season)
    {
        return new()
        {
            ["Id"] = season.BattlePassId,
            ["SeasonId"] = season.Id,
            ["ExchangeRate"] = season.ExchangeRate,
            ["ItemExchange"] = new JObject { ["itemId"] = season.ExchangeCrate, ["requiredDocuments"] = season.CrateCost },
            ["Documents"] = new JArray(season.Documents.Select(d => new JObject { ["id"] = d.Id, ["itemId"] = d.ItemId })),
            ["Rewards"] = new JObject(
                season
                    .AllRewards.Where(r => r.Enabled)
                    .Select(r => new JProperty(
                        r.Id,
                        new JObject { ["Grants"] = r.Grants.DeepClone(), ["Conditions"] = r.Conditions.DeepClone() }
                    ))
            ),
            ["Offers"] = season.Offers.DeepClone(),
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
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct();
    }

    public static JObject GameplayIdentity(SeasonDefinition season)
    {
        var value = JObject.FromObject(season);
        foreach (
            var key in new[]
            {
                "Name",
                "Description",
                "Author",
                "Version",
                "Revision",
                "Branding",
                "Locales",
                "Slides",
                "UniversalImage",
                "UniversalUnavailableImage",
            }
        )
        {
            value.Remove(key);
        }
        // Remove only presentation fields at known paths. Never strip a nested grant's semantic name/value.
        foreach (var perk in ((JObject)value["Perks"]!).Properties().SelectMany(p => p.Value.OfType<JObject>()))
        {
            perk.Remove("imageUrl");
        }

        foreach (var doc in value["Documents"]!.OfType<JObject>())
        {
            foreach (var key in new[] { "Name", "Image", "UnavailableImage" })
            {
                doc.Remove(key);
            }
        }

        foreach (var reward in value["Pages"]!.SelectMany(p => p["Rewards"]!).Concat(value["SeasonalRewards"]!).OfType<JObject>())
        {
            foreach (var key in new[] { "Name", "Description", "Image", "BigImage", "Kind", "Requirements" })
            {
                reward.Remove(key);
            }
        }

        foreach (var item in value["Items"]!.OfType<JObject>())
        {
            item.Remove("Name");
            item.Remove("Description");
        }
        foreach (var quest in value["Quests"]!.OfType<JObject>())
        {
            foreach (
                var key in new[]
                {
                    "localization",
                    "QuestName",
                    "name",
                    "description",
                    "image",
                    "startedMessageText",
                    "successMessageText",
                    "failMessageText",
                    "acceptPlayerMessage",
                    "completePlayerMessage",
                    "declinePlayerMessage",
                }
            )
            {
                quest.Remove(key);
            }
        }

        return value;
    }

    public static JToken Canonical(JToken token)
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
}
