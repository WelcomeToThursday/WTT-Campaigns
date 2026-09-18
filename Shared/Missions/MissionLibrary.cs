using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>Marks an independent mission document in the shared content-pack storage.</summary>
public sealed class MissionPackage
{
    public int Version { get; set; } = 1;
    public bool AllowStandalonePlay { get; set; }
}

[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum MissionAvailability { FromStart, QuestAccepted, QuestCompleted, ChapterReached, StoryAction }

public sealed class CampaignMissionLink
{
    public string Id { get; set; } = "";
    public string MissionId { get; set; } = "";
    public long Revision { get; set; }
    public string ContentHash { get; set; } = "";
    // A frozen package travels with the campaign, including its asset references.
    public SeasonDefinition Package { get; set; } = new();
    public MissionAvailability Availability { get; set; }
    public string UnlockTargetId { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string CompletionConditionId { get; set; } = "";
    public string LegacyMissionId { get; set; } = "";
}

public static class MissionLibrary
{
    public const int FormatVersion = 11;
    public const string StandaloneScope = "standalone";

    public static bool Eligible(CampaignMissionLink link, StoryDefinition? story, StoryProgress progress, StoryFacts facts)
    {
        var status = facts.QuestStatuses.GetValueOrDefault(link.UnlockTargetId) ?? "";
        return link.Availability switch
        {
            MissionAvailability.FromStart => true,
            MissionAvailability.QuestAccepted => status is "Started" or "AvailableForFinish" or "Success",
            MissionAvailability.QuestCompleted => status == "Success",
            MissionAvailability.ChapterReached => story?.Chapters.FirstOrDefault(c => c.Id == link.UnlockTargetId) is { } chapter
                && StoryRules.Evaluate(chapter.Visibility, story, progress, facts),
            MissionAvailability.StoryAction => progress.UnlockedMissionLinks.Contains(link.Id),
            _ => false,
        };
    }

    public static string LockReason(CampaignMissionLink link) => link.Availability switch
    {
        MissionAvailability.QuestAccepted => "Accept the linked story quest to unlock this mission.",
        MissionAvailability.QuestCompleted => "Complete the linked story quest to unlock this mission.",
        MissionAvailability.ChapterReached => "Reach the linked story chapter to unlock this mission.",
        MissionAvailability.StoryAction => "Continue the story to unlock this mission.",
        _ => "Available from the start.",
    };

    public static void ValidatePackage(SeasonDefinition package, SeasonValidationResult result)
    {
        void Need(bool ok, string message) { if (!ok) result.Add("Missions", message); }
        Need(package.FormatVersion == FormatVersion && package.MissionPackage?.Version == 1, "Unsupported mission package format.");
        Need(SeasonValidator.IsId(package.Id), "Mission package identity is invalid.");
        Need(!string.IsNullOrWhiteSpace(package.Name) && package.Name.Length <= 120, "Enter a mission package name (up to 120 characters).");
        Need(package.MissionLinks.Count == 0 && package.Story == null && package.Quests.Count == 0, "Independent missions cannot contain campaign links or story quests.");
        Need(package.Documents.Count == 0 && package.AllRewards.Count() == 0 && package.TraderOffers.Count == 0 && package.TraderAssorts.Count == 0
            && package.Offers.Count == 0 && package.Crates.Count == 0 && package.Crafts.Count == 0 && package.QuestLoot.Count == 0 && !package.Legacy,
            "Mission packages contain mission content, not campaign rewards, traders or progression.");
        Need(package.Dependencies.Count <= 1000 && package.Items.Count <= 1000 && package.ImportedItems.Count <= 1000, "Mission package content exceeds its limits.");
        Need(package.Items.All(i => SeasonValidator.IsId(i.Id) && SeasonValidator.IsId(i.CloneFrom)) && package.Items.Select(i => i.Id).Distinct().Count() == package.Items.Count,
            "Mission item definitions require unique valid identities and source templates.");
        Need(package.Zones.All(z => package.MapLayouts.Any(l => l.Id == z.LayoutId && l.Location == z.Location)), "Mission zones must belong to the package layout and map.");
        Need(package.Missions.Count == 1, "A mission package contains exactly one mission.");
        Need(package.MapLayouts.Count == 1, "A mission package contains exactly one layout.");
        if (package.Missions.Count != 1 || package.MapLayouts.Count != 1) return;
        var mission = package.Missions[0];
        var layout = package.MapLayouts[0];
        Need(SeasonValidator.IsId(mission.Id) && mission.Id == package.Id && mission.LayoutId == layout.Id, "Choose the package's mission layout.");
        Need(mission.QuestId.Length == 0 && mission.CompletionConditionId.Length == 0, "Quest bindings belong to campaign links.");
        Need(!string.IsNullOrWhiteSpace(mission.Name) && mission.Name.Length <= 120, "Enter a mission name (up to 120 characters).");
        Need(!string.IsNullOrWhiteSpace(mission.Briefing) && mission.Briefing.Length <= 4000, "Enter a mission briefing (up to 4000 characters).");
        Need(!layout.ApplyInNormalRaids, "Mission layouts cannot apply in normal raids.");
        foreach (var error in Spatial.MapLayoutRules.Errors(layout, walkthrough: true).Concat(MissionLogicRules.Errors(mission, layout)))
            result.Add("Missions", error);
        foreach (var error in Spatial.SpatialRules.Errors(package)) result.Add("Zones", error);
    }

    public static void ValidateLinks(SeasonDefinition campaign, SeasonValidationResult result)
    {
        if (campaign.MissionLinks.Count > 1000) { result.Add("MissionLinks", "Use at most 1000 mission links."); return; }
        var ids = new HashSet<string>(campaign.Missions.Select(m => m.Id));
        var missions = new HashSet<string>();
        foreach (var link in campaign.MissionLinks)
        {
            var path = "MissionLinks/" + link.Id;
            void Need(bool ok, string message) { if (!ok) result.Add(path, message); }
            Need(campaign.FormatVersion >= FormatVersion, "Mission links require format 11.");
            Need(SeasonValidator.IsId(link.Id) && ids.Add(link.Id), "Mission link identities must be unique.");
            Need(missions.Add(link.MissionId), "Link a mission only once per campaign.");
            Need(link.Package.MissionPackage != null && link.Package.MissionLinks.Count == 0, "Choose an independent mission package.");
            if (link.Package.MissionPackage == null || link.Package.MissionLinks.Count != 0) continue;
            ValidatePackage(link.Package, result);
            Need(link.Revision > 0 && link.Revision == link.Package.Revision && link.Package.Missions.Count == 1 && link.Package.Missions[0].Id == link.MissionId,
                "The pinned mission identity or revision does not match its package.");
            Need(link.ContentHash == PackageHash(link.Package), "The pinned mission checksum does not match.");
            Need(Enum.IsDefined(typeof(MissionAvailability), link.Availability), "Choose a supported availability mode.");
            if (link.Availability is MissionAvailability.QuestAccepted or MissionAvailability.QuestCompleted)
                Need(campaign.Story?.Quests.Any(q => q.QuestId == link.UnlockTargetId) == true, "Choose a story quest for unlocking.");
            if (link.Availability == MissionAvailability.ChapterReached)
                Need(campaign.Story?.Chapters.Any(c => c.Id == link.UnlockTargetId) == true, "Choose a story chapter for unlocking.");
            if (link.QuestId.Length > 0)
            {
                Need(campaign.Story?.Quests.Any(q => q.QuestId == link.QuestId) == true, "The completion quest must belong to this story.");
                var condition = campaign.Quests.FirstOrDefault(q => q.Id == link.QuestId)?.Conditions.AvailableForFinish.FirstOrDefault(c => c.Id == link.CompletionConditionId);
                var variable = condition?.Target?.Values is { Count: 1 } values ? campaign.Story?.Variables.FirstOrDefault(v => v.Id == values[0]) : null;
                Need(condition?.ConditionType == "GlobalVariableValue" && condition.Value is >= 1 and <= 1000000
                    && (condition.CompareMethod == null || condition.CompareMethod == ">=") && variable?.Scope == StoryVariableScope.Profile && variable.InitialValue == 0,
                    "Choose a completion objective backed by a zero-initialized profile variable.");
            }
            else Need(link.CompletionConditionId.Length == 0, "Choose a quest before its completion objective.");
        }
    }

    public static IEnumerable<string> ReferencedItems(SeasonDefinition package) => package.MapLayouts.SelectMany(layout =>
        layout.Loot.SelectMany(l => l.Items).Select(i => i.Template)
            .Concat(layout.Doors.Select(d => d.KeyId ?? ""))
            .Concat(layout.Objects.Select(o => o.Target.Template))
            .Concat(layout.Objects.Where(o => o.Container != null).SelectMany(o => o.Container!.Contents.Select(c => c.Template).Append(o.Container.KeyTemplate))))
        .Concat(package.Zones.Where(z => z.Uses.Contains("Salvage")).SelectMany(z => z.Salvage.Rewards.Select(r => r.ItemTpl).Append(z.Salvage.RequiredItemTpl)))
        .Where(SeasonValidator.IsId).Distinct();

    public static string PackageHash(SeasonDefinition package)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(SeasonCompiler.GameplayIdentity(package)))).Replace("-", "").ToLowerInvariant();
    }

    public static SeasonDefinition Standalone(IEnumerable<SeasonDefinition> published, MissionRun? active)
    {
        var all = published.ToList();
        var packages = all.GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => p.Revision).First())
            .Where(p => p.MissionPackage?.AllowStandalonePlay == true).ToList();
        if (active is { ContextVersion: 3, Scope: StandaloneScope } && !MissionRunStatuses.IsTerminal(active.Status))
        {
            var pinned = all.FirstOrDefault(p => p.Id == active.PackageId && p.Revision == active.PackageRevision);
            packages.RemoveAll(p => p.Id == active.PackageId);
            if (pinned != null) packages.Add(pinned);
        }
        var definition = new SeasonDefinition { Id = StandaloneScope };
        foreach (var package in packages)
            definition.MissionLinks.Add(new CampaignMissionLink
            {
                Id = package.Missions.Single().Id, MissionId = package.Missions.Single().Id,
                Revision = package.Revision, Package = package, ContentHash = PackageHash(package),
            });
        return Resolve(definition);
    }

    public static SeasonDefinition Resolve(SeasonDefinition source)
    {
        var output = SeasonCompiler.Copy(source);
        foreach (var link in source.MissionLinks)
        {
            var package = SeasonCompiler.Copy(link.Package);
            var owned = package.MapLayouts.SelectMany(Spatial.MapLayoutRules.OwnedIds)
                .Concat(package.Zones.Select(z => z.Id))
                .Concat(package.Missions.SelectMany(m => m.Events.Select(e => e.Id).Concat(m.Objectives.Select(o => o.Id)))).Distinct();
            using var sha = System.Security.Cryptography.SHA256.Create();
            var replacements = owned.ToDictionary(id => id, id => BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(link.Id + ":" + id))).Replace("-", "").ToLowerInvariant().Substring(0, 24));
            Serialization.ModelGraph.Rewrite(package, text =>
            {
                if (replacements.TryGetValue(text, out var value)) return value;
                var colon = text.IndexOf(':');
                return colon == 24 && replacements.TryGetValue(text.Substring(0, colon), out value) ? value + text.Substring(colon) : text;
            });
            var mission = package.Missions.Single();
            mission.Id = link.Id;
            mission.QuestId = link.QuestId;
            mission.CompletionConditionId = link.CompletionConditionId;
            output.Missions.Add(mission);
            output.MapLayouts.AddRange(package.MapLayouts);
            output.Zones.AddRange(package.Zones);
        }
        return output;
    }
}
