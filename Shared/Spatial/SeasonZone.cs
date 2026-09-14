using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Spatial;

public sealed class SpatialVector
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    [Newtonsoft.Json.JsonIgnore]
    public bool Finite
    {
        get
        {
            return !(
                float.IsNaN(X) || float.IsInfinity(X) || float.IsNaN(Y) || float.IsInfinity(Y) || float.IsNaN(Z) || float.IsInfinity(Z)
            );
        }
    }
}

public class SpatialCapture
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New capture";
    public string Location { get; set; } = "";
    public string Scene { get; set; } = "";
    public SpatialVector Position { get; set; } = new();
    public SpatialVector Rotation { get; set; } = new();
    public string ObjectPath { get; set; } = "";
}

public sealed class SeasonZone : SpatialCapture
{
    public SalvageZoneSettings Salvage { get; set; } = new();
    public string Shape { get; set; } = "Box";
    public SpatialVector Size { get; set; } =
        new()
        {
            X = 3,
            Y = 3,
            Z = 3,
        };
    public float Radius { get; set; } = 2;

    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<string> Uses { get; set; } = new() { "InZone", "VisitPlace" };
}

public sealed class SalvageZoneSettings
{
    public string RequiredItemTpl { get; set; } = "";
    public float SalvageTime { get; set; } = 10;
    public bool ConsumeRequiredItem { get; set; } = true;
    public List<SalvageZoneReward> Rewards { get; set; } = new();
}

public sealed class SalvageZoneReward
{
    public string ItemTpl { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool ToQuestInventory { get; set; }
}

public static class SpatialRules
{
    public static IEnumerable<NativeCondition> Conditions(SeasonDefinition season)
    {
        return ModelGraph.Texts(season.Quests).SelectMany(t => t.Ancestors.OfType<NativeCondition>()).Distinct();
    }

    public static IEnumerable<string> References(NativeCondition c)
    {
        return c.ConditionType == "VisitPlace" ? c.Target?.Values ?? Enumerable.Empty<string>()
            : c.ConditionType == "InZone" ? c.ZoneIds ?? Enumerable.Empty<string>()
            : c.ZoneId == null ? Enumerable.Empty<string>()
            : new[] { c.ZoneId };
    }

    public static void Assign(NativeCondition condition, string id)
    {
        if (condition.ConditionType == "VisitPlace")
        {
            condition.Target = id;
            condition.ZoneId = null;
        }
        else if (condition.ConditionType == "InZone")
        {
            condition.ZoneIds = new() { id };
            condition.ZoneId = null;
        }
        else
        {
            condition.ZoneId = id;
        }
    }

    public static IEnumerable<string> Uses(SeasonDefinition season, string id)
    {
        return (
            season.Story?.RaidBindings.Where(b => b.ZoneId == id).Select(b => "Raid event " + b.Id) ?? Enumerable.Empty<string>()
        ).Concat(Conditions(season).Where(c => References(c).Contains(id)).Select(c => "Objective " + c.Id));
    }

    public static List<string> Errors(SeasonDefinition season, bool requireCompleteSalvage = true)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>();
        if (season.Zones.Count > 2000 || season.Captures.Count > 2000)
        {
            errors.Add("At most 2000 zones and 2000 captures are supported.");
        }

        foreach (var point in season.Zones.Cast<SpatialCapture>().Concat(season.Captures))
        {
            if (!SeasonValidator.IsId(point.Id) || !ids.Add(point.Id))
            {
                errors.Add("Invalid or duplicate spatial identity: " + point.Id);
            }

            if (
                string.IsNullOrWhiteSpace(point.Name)
                || point.Name.Length > 120
                || string.IsNullOrWhiteSpace(point.Location)
                || string.IsNullOrWhiteSpace(point.Scene)
            )
            {
                errors.Add("Name, map and scene are required: " + point.Id);
            }

            if (point.Position?.Finite != true || point.Rotation?.Finite != true)
            {
                errors.Add("Transforms must be finite: " + point.Id);
            }

            if (point is SeasonZone zone)
            {
                if (zone.Shape is not ("Box" or "Sphere"))
                {
                    errors.Add("Unknown zone shape: " + zone.Id);
                }

                if (
                    zone.Size?.Finite != true
                    || zone.Size.X <= 0
                    || zone.Size.Y <= 0
                    || zone.Size.Z <= 0
                    || !float.IsFinite(zone.Radius)
                    || zone.Radius <= 0
                )
                {
                    errors.Add("Zone dimensions must be positive and finite: " + zone.Id);
                }

                if (zone.Uses == null || zone.Uses.Any(u => u is not ("InZone" or "VisitPlace" or "LeaveItemAtLocation" or "Salvage")))
                {
                    errors.Add("Unsupported quest zone use: " + zone.Id);
                }
                if (zone.Uses?.Contains("Salvage") == true)
                {
                    var salvage = zone.Salvage;
                    if (
                        requireCompleteSalvage
                        && (
                            salvage == null
                            || !SeasonValidator.IsId(salvage.RequiredItemTpl)
                            || !float.IsFinite(salvage.SalvageTime)
                            || salvage.SalvageTime <= 0
                            || salvage.Rewards == null
                            || salvage.Rewards.Any(r => r == null || !SeasonValidator.IsId(r.ItemTpl) || r.Count <= 0)
                        )
                    )
                        errors.Add(
                            "Salvage requires an item, a positive finite interaction time and valid reward items/counts: " + zone.Id
                        );
                    if (zone.Uses.Contains("LeaveItemAtLocation"))
                        errors.Add("Salvage and item placement require separate zones: " + zone.Id);
                }
            }
        }
        foreach (var binding in season.Story?.RaidBindings ?? new())
        {
            if (binding.ZoneId.Length == 0)
            {
                continue;
            }

            var zone = season.Zones.FirstOrDefault(z => z.Id == binding.ZoneId);
            if (
                zone == null
                || zone.Location != binding.Location
                || binding.Kind is not ("Trigger" or "Cinematic")
                || binding.ObjectPath.Length > 0
            )
            {
                errors.Add("A zone binding requires a matching map, Trigger/Cinematic kind and no object path: " + binding.Id);
            }
        }
        foreach (var condition in Conditions(season))
        {
            foreach (var zone in season.Zones.Where(z => References(condition).Contains(z.Id)))
            {
                if (!zone.Uses.Contains(condition.ConditionType))
                {
                    errors.Add("Zone does not support " + condition.ConditionType + ": " + zone.Id);
                }
            }
        }

        if ((season.Zones.Count > 0 || season.Captures.Count > 0) && season.FormatVersion is not (2 or 3 or 4 or 5))
        {
            errors.Add("Spatial content requires campaign format 2 or later.");
        }

        return errors;
    }
}
