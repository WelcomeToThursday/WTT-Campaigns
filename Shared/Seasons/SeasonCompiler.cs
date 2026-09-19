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

        foreach (var mission in season.Missions)
        {
            texts[mission.Id + " name"] = mission.Name;
            texts[mission.Id + " briefing"] = mission.Briefing;
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
        var owned = season.Items.AsValueEnumerable().Select(i => i.Id).Concat(season.ImportedItems.Keys).ToHashSet();
        return Enumerate();
        IEnumerable<string> Enumerate()
        {
            var items = season
                .Items.AsValueEnumerable()
                .Select(i => i.CloneFrom)
                .Concat(Missions.MissionLibrary.ReferencedItems(season))
                .Concat(season.Documents.AsValueEnumerable().Select(d => d.ItemId))
                .Concat(season.Crates.AsValueEnumerable().SelectMany(c => c.Pool.Keys))
                .Concat(season.QuestLoot.AsValueEnumerable().Select(l => l.ItemTemplate))
                .Concat(season.Crafts.AsValueEnumerable().Select(c => c.EndProduct))
                .Concat(
                    season
                        .Crafts.AsValueEnumerable()
                        .SelectMany(c => c.Requirements)
                        .Where(r => r.Type == "Item")
                        .Select(r => r.TemplateId!)
                )
                .Concat(
                    season
                        .TraderOffers.AsValueEnumerable()
                        .SelectMany(o =>
                            o.Items.AsValueEnumerable()
                                .Select(i => i.Template)
                                .Concat(o.Barter.AsValueEnumerable().SelectMany(b => b).Select(b => b.Template))
                        )
                )
                .Concat(
                    season
                        .Zones.AsValueEnumerable()
                        .Where(z => z.Uses.Contains("Salvage"))
                        .SelectMany(z => z.Salvage.Rewards.AsValueEnumerable().Select(r => r.ItemTpl).Append(z.Salvage.RequiredItemTpl))
                )
                .Concat(
                    new[] { season.Starting.Usec, season.Starting.Bear }
                        .AsValueEnumerable()
                        .SelectMany(f => f.Items.AsValueEnumerable().Select(i => i.Template))
                )
                .Concat(
                    season
                        .AllRewards.AsValueEnumerable()
                        .Where(r => r.Enabled)
                        .SelectMany(r => r.Grants)
                        .SelectMany(g => g.Items)
                        .Concat(season.Quests.AsValueEnumerable().Where(q => q.SeasonalEnabled != false).SelectMany(q => q.AllItems()))
                        .Select(i => i.Template)
                )
                .ToArray();
            foreach (
                var dependency in season
                    .Dependencies.AsValueEnumerable()
                    .Concat(season.MissionLinks.AsValueEnumerable().SelectMany(l => Dependencies(l.Package)))
                    .Concat(items.AsValueEnumerable().Where(i => !owned.Contains(i)).Select(i => "item:" + i))
                    .Distinct()
                    .OrderBy(i => i, StringComparer.Ordinal)
                    .ToArray()
            )
                yield return dependency;
        }
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
                .Documents.AsValueEnumerable()
                .Select(d => new HubDocument
                {
                    Id = d.Id,
                    Name = d.Name,
                    Image = d.Image,
                    UnavailableImage = d.UnavailableImage,
                    ItemId = d.ItemId,
                })
                .ToArray(),
            Pages = season
                .Pages.AsValueEnumerable()
                .Select(p => new HubPage
                {
                    PreviousRequirement = p.PreviousRequirement,
                    Rewards = p.Rewards.AsValueEnumerable().Where(r => r.Enabled).Select(Tile).ToArray(),
                })
                .ToArray(),
            SeasonalRewards = season.SeasonalRewards.AsValueEnumerable().Where(r => r.Enabled).Select(Tile).ToArray(),
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
            Documents = season.Documents.AsValueEnumerable().Select(d => new HubGameplayDocument { Id = d.Id, ItemId = d.ItemId }).ToList(),
            Rewards = season
                .AllRewards.AsValueEnumerable()
                .Where(r => r.Enabled)
                .ToDictionary(r => r.Id, r => new HubGameplayReward { Grants = Copy(r.Grants), Conditions = Copy(r.Conditions) }),
            Offers = Copy(season.Offers),
            TraderOffers = Copy(season.TraderOffers),
            TraderAssorts = Copy(season.TraderAssorts),
        };
    }

    public static IEnumerable<string> Assets(SeasonDefinition season)
    {
        var seen = new HashSet<string>();
        foreach (var asset in Candidates())
            if (!string.IsNullOrEmpty(asset) && seen.Add(asset))
                yield return asset;

        IEnumerable<string> Candidates()
        {
            foreach (var perk in season.Perks.All)
                yield return perk.ImageUrl;
            foreach (var document in season.Documents)
            {
                yield return document.Image;
                yield return document.UnavailableImage;
            }
            foreach (var reward in season.AllRewards)
            {
                yield return reward.Image;
                yield return reward.BigImage;
            }
            yield return season.UniversalImage;
            yield return season.UniversalUnavailableImage;
            yield return season.Branding.Badge;
            yield return season.Branding.Banner;
            foreach (var slide in season.Slides)
                yield return slide.Image;
            if (season.Story != null)
                foreach (var chapter in season.Story.Chapters)
                {
                    yield return chapter.Image;
                    yield return chapter.Icon;
                }
            foreach (var link in season.MissionLinks)
            foreach (var asset in Assets(link.Package))
                yield return asset;
        }
    }

    public static string GameplayIdentity(SeasonDefinition season)
    {
        return GameplaySerialization.Identity(season);
    }
}
