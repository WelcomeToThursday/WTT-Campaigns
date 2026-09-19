using Newtonsoft.Json;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class ZoneLayoutChecks
{
    private static string Id() => Guid.NewGuid().ToString("N").Substring(0, 24);

    private static SeasonZone Zone(string location, string layoutId = "") =>
        new()
        {
            Id = Id(),
            Name = "Zone",
            Location = location,
            Scene = location + "_main",
            LayoutId = layoutId,
        };

    private static MapLayout Layout(string location) =>
        new()
        {
            Id = Id(),
            Name = "Layout",
            Location = location,
        };

    internal static void Run(Action<bool, string> check)
    {
        var first = Layout("woods");
        var second = Layout("woods");
        var unrelatedLayout = Layout("woods");
        var otherMap = Layout("factory4_day");
        var shared = Zone("woods");
        var owned = Zone("woods", first.Id);
        var other = Zone("woods", unrelatedLayout.Id);
        var season = new SeasonDefinition
        {
            FormatVersion = 4,
            MapLayouts = new() { first, second, unrelatedLayout, otherMap },
            Zones = new() { shared, owned, other },
        };

        var reloaded = JsonConvert.DeserializeObject<SeasonDefinition>(JsonConvert.SerializeObject(season))!;
        check(
            reloaded.Zones[0].LayoutId.Length == 0 && reloaded.Zones.Single(z => z.Id == owned.Id).LayoutId == first.Id,
            "Zone layout ownership survives JSON roundtrip and missing ownership stays Shared"
        );
        var legacy = JsonConvert.DeserializeObject<SeasonZone>("{ 'Id': '" + Id() + "', 'Location': 'woods', 'Scene': 'woods_main' }")!;
        check(ZoneLayoutRules.IsShared(legacy), "Zones without LayoutId load as Shared for legacy campaigns");

        check(
            ZoneLayoutRules.ForEditor(season.Zones, first.Id).Select(z => z.Id).SequenceEqual(new[] { shared.Id, owned.Id }),
            "Editor layout filtering includes the current layout and Shared zones"
        );
        check(
            ZoneLayoutRules.ForEditor(season.Zones, first.Id, includeShared: false).Single().Id == owned.Id,
            "Editor filtering can hide Shared zones when a layout-only list is needed"
        );
        check(
            ZoneLayoutRules.ForEditor(season.Zones, "").Single().Id == shared.Id,
            "Shared filtering does not accidentally include another layout"
        );

        var missingOwner = Zone("woods", Id());
        season.Zones.Add(missingOwner);
        check(ZoneLayoutRules.Errors(season).Any(e => e.Contains(missingOwner.Id)), "Zone ownership validation rejects a missing layout");
        season.Zones.Remove(missingOwner);
        owned.LayoutId = otherMap.Id;
        check(ZoneLayoutRules.Errors(season).Any(e => e.Contains(owned.Id)), "Zone ownership validation rejects a layout on another map");
        owned.LayoutId = first.Id;
        check(ZoneLayoutRules.Errors(season).Count == 0, "A zone owned by a same-map layout passes ownership validation");

        var copied = ZoneLayoutRules.CopyOwnedZones(season, first.Id, second.Id);
        check(
            copied.Count == 1 && copied[0].Id != owned.Id && copied[0].LayoutId == second.Id && season.Zones.All(z => z.Id != copied[0].Id),
            "Layout duplication returns fresh-ID copies without mutating the source season"
        );
        season.Zones.AddRange(copied);
        var duplicateRejected = false;
        try
        {
            ZoneLayoutRules.CopyOwnedZones(season, first.Id, second.Id);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        check(duplicateRejected, "A destination layout cannot receive a second owned-zone copy");

        season.Story = new StoryDefinition();
        season.Story.RaidBindings.Add(
            new StoryRaidBinding
            {
                Id = Id(),
                Name = "Zone event",
                Location = "woods",
                Kind = "Trigger",
                ZoneId = owned.Id,
            }
        );
        var beforeDelete = JsonConvert.SerializeObject(season);
        check(
            !ZoneLayoutRules.TryDeleteLayout(season, first.Id, out var blocked)
                && blocked.Contains("Raid event")
                && JsonConvert.SerializeObject(season) == beforeDelete,
            "Referenced layout zones block deletion without a partial mutation"
        );
        season.Story.RaidBindings.Clear();
        check(
            ZoneLayoutRules.TryDeleteLayout(season, first.Id, out _)
                && season.MapLayouts.All(l => l.Id != first.Id)
                && season.Zones.All(z => z.LayoutId != first.Id),
            "An unreferenced layout deletes its own zones atomically"
        );

        var conditionZone = Zone("woods", second.Id);
        season.Zones.Add(conditionZone);
        var quest = new NativeQuest { Id = Id() };
        quest.Conditions.AvailableForFinish.Add(
            new NativeCondition
            {
                Id = Id(),
                ConditionType = "InZone",
                ZoneIds = new() { conditionZone.Id },
            }
        );
        season.Quests.Add(quest);
        check(
            !ZoneLayoutRules.TryDeleteLayout(season, second.Id, out var conditionBlocked) && conditionBlocked.Contains("Objective"),
            "Native quest conditions also protect owned zones from deletion"
        );
        check(SpatialRules.Errors(season).Count == 0, "Draft spatial validation permits a referenced layout zone while authoring");
        var publication = SeasonValidator.Validate(season);
        check(
            publication.Issues.Any(issue => issue.Path == "Zones/" + conditionZone.Id && issue.Severity == "error"),
            "Publication validation rejects live quest references to layout zones"
        );
        second.ApplyInNormalRaids = true;
        var levelPublication = SeasonValidator.Validate(season);
        check(!levelPublication.Issues.Any(issue => issue.Path == "Zones/" + conditionZone.Id && issue.Severity == "error"),
            "Enabled ordinary-raid levels provide their native quest zones during publication");
        second.ApplyInNormalRaids = false;
        conditionZone.LayoutId = "";
        var sharedPublication = SeasonValidator.Validate(season);
        check(
            !sharedPublication.Issues.Any(issue =>
                issue.Path == "Zones/" + conditionZone.Id && issue.Message.Contains("layout raids are supported", StringComparison.Ordinal)
            ),
            "Publication validation allows the same quest reference when the zone is Shared"
        );
    }
}
