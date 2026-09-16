using Newtonsoft.Json;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Seasons;

internal static class BuiltInCampaignCopy
{
    internal const string HistoricalPerspectives = "6a4f82e11b7350af050b2e1c";

    // These 1.0 hideout/menu cosmetics are absent from the supported backport.
    // Retain the cards for authoring, but never sell an unlock with no content.
    private static readonly HashSet<string> UnavailableCosmetics = new()
    {
        "6a3bb457200c84aede0f3f8d",
        "6a3575489725a9cf0c0907ed",
        "6a3bb5827182e638dc05bd2a",
        "6a355d50ac2172951002b9d9",
        "6a35762dd864773668046a6d",
        "6a3bb59dae4fbdbb95040aab",
        "6a355ee32077357a160ff7a6",
        "6a355dc00520e4a0b00859a8",
        "6a59f553acaee8fa5d013979",
    };

    internal static SeasonDefinition Prepare(SeasonDefinition source)
    {
        if (!source.Legacy || source.Id != "69e232a764dfe95549003f0f")
        {
            return source;
        }
        using var stream =
            typeof(BuiltInCampaignCopy).Assembly.GetManifestResourceStream("WTT.Campaigns.KordCopy.json")
            ?? throw new InvalidDataException("The built-in campaign authoring template is missing.");
        using var reader = new StreamReader(stream);
        var template = JsonConvert.DeserializeObject<SeasonDefinition>(reader.ReadToEnd())!;
        var copy = SeasonCompiler.Copy(source);
        copy.FormatVersion = template.FormatVersion;
        copy.Quests = template.Quests;
        copy.Zones = template.Zones;
        copy.Story = template.Story;
        copy.QuestLoot = template.QuestLoot;
        copy.Crafts = template.Crafts;
        copy.TraderOffers.AddRange(template.TraderOffers);
        copy.Items.AddRange(template.Items);
        copy.Dependencies.AddRange(template.Dependencies);
        foreach (
            var reward in copy.AllRewards.Where(r =>
                r.Grants.Any(g => g.Type == "CustomizationDirect" && UnavailableCosmetics.Contains(g.Target ?? ""))
            )
        )
        {
            reward.Enabled = false;
            reward.Requirements.Add(
                "This EFT 1.0 hideout or menu cosmetic is not supplied by the supported backport. Enable after installing its content."
            );
        }
        foreach (
            var reward in copy.AllRewards.Where(reward =>
                reward.Conditions.Any(c => c.ConditionType == "Quest" && c.Target?.Contains(HistoricalPerspectives) == true)
            )
        )
        {
            reward.Enabled = false;
            reward.Requirements.Add("Historical Perspectives is unreleased. This reward remains unavailable.");
        }
        for (var page = 1; page < copy.Pages.Count; page++)
            copy.Pages[page].PreviousRequirement = Math.Min(
                copy.Pages[page].PreviousRequirement,
                copy.Pages[page - 1].Rewards.Count(r => r.Enabled)
            );
        return copy;
    }
}
