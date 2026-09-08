using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Progression;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Tests;

internal static class NativeModelChecks
{
    public static void Run(Action<bool, string> check)
    {
        void RoundTrip<T>(string file)
        {
            var source = JToken.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file)));
            var typed = source.ToObject<T>()!;
            check(JToken.DeepEquals(source, JToken.FromObject(typed)), "Typed import preserves every captured field: " + file);
            check(JToken.DeepEquals(source, JToken.FromObject(SeasonCompiler.Copy(typed)!)), "Typed copy preserves wire shapes: " + file);
        }
        RoundTrip<List<NativeQuest>>("hub-quests.json");
        RoundTrip<Dictionary<string, NativeItemTemplate>>("season-items.json");
        foreach (var location in new[] { "2", "{\"x\":1,\"y\":2,\"r\":0}", "{\"x\":1,\"y\":2,\"r\":\"Vertical\",\"isSearched\":true}" })
        {
            var rawItem = JObject.Parse("{\"_id\":\"item\",\"_tpl\":\"template\",\"location\":" + location + "}");
            var item = rawItem.ToObject<NativeItem>()!;
            check(JToken.DeepEquals(rawItem, JToken.FromObject(item)), "Indexed and grid item locations retain their native wire shape");
            if (item.Location!.Grid is { } grid)
            {
                grid.X = 5;
                check((int)JToken.FromObject(item)["location"]!["x"]! == 5, "Typed grid edits survive serialization");
            }
        }
        var fractional = JsonConvert.DeserializeObject<NativeCondition>(
            """
            {"conditionType":"Kills","value":1,"distance":{"value":12.5,"compareMethod":">="},"minDurability":90.5,"enemyHealthEffects":[{"bodyParts":["Head"],"effects":["Pain"]}]}
            """
        )!;
        check(
            fractional.Distance!.Value == 12.5
                && fractional.MinDurability == 90.5
                && fractional.EnemyHealthEffects![0].Effects![0] == "Pain",
            "Native objective models support fractional thresholds and structured health effects"
        );
        var progressionSource = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "trader-progression.json")));
        var progression = progressionSource.ToObject<TraderProgression>()!;
        foreach (var pair in progression.Quests)
        {
            check(
                JToken.DeepEquals(progressionSource["Quests"]![pair.Key]!["Start"], JToken.FromObject(pair.Value.Start)),
                "Captured start conditions round trip: " + pair.Key
            );
            check(
                JToken.DeepEquals(progressionSource["Quests"]![pair.Key]!["Reputation"], JToken.FromObject(pair.Value.Reputation)),
                "Captured reputation grants round trip: " + pair.Key
            );
        }

        const string raw = """
            {"id":"old","conditionType":"Quest","target":"quest","value":"4","status":[4],"hint":null,"future":{"nested":[1,"x",null]}}
            """;
        var condition = JsonConvert.DeserializeObject<NativeCondition>(raw)!;
        check(
            condition.Value == 4 && condition.Status!.SequenceEqual(["4"]) && (string?)condition.Target == "quest",
            "Native scalar fields deserialize to concrete types"
        );
        check(
            JToken.DeepEquals(JToken.Parse(raw), JToken.FromObject(condition)),
            "Numeric strings, numeric statuses, nulls and future fields round trip exactly"
        );
        condition.Value = 6;
        condition.Target = new[] { "next", "other" };
        var edited = JToken.FromObject(condition);
        check(
            (double)edited["value"]! == 6 && edited["target"] is JArray { Count: 2 },
            "Typed edits replace numeric strings and scalar targets correctly"
        );
        check(JToken.DeepEquals(edited["future"], JToken.Parse(raw)["future"]), "Typed edits preserve opaque future fields");
        var copy = SeasonCompiler.Copy(condition);
        copy.Target!.Values[0] = "copy";
        check(condition.Target.Values[0] == "next", "Native copies do not share editable collections");

        var quest = JsonConvert.DeserializeObject<NativeQuest>(
            """
            {"_id":"quest","name":"original","localization":{"en":{"quest name":"original"}},"conditions":{"AvailableForStart":[],"AvailableForFinish":[],"Fail":[]},"rewards":{}}
            """
        )!;
        var season = new SeasonDefinition { Quests = [quest] };
        var identity = SeasonCompiler.GameplayIdentity(season);
        quest.Name = "renamed";
        quest.Localization["en"]["quest name"] = "renamed";
        check(
            SeasonCompiler.GameplayIdentity(season) == identity,
            "Typed hash excludes native presentation fields without changing imported shape"
        );
        quest.Conditions.AvailableForStart.Add(condition);
        check(SeasonCompiler.GameplayIdentity(season) != identity, "Typed hash retains semantic objective changes");
        condition.Props = new NativeCondition
        {
            VisibilityConditions = [new NativeCondition { Id = "visibility", ConditionType = "CompletableItem" }],
        };
        check(
            quest.AllConditions().Any(c => c.Id == "visibility") && quest.AllConditions().All(c => c.ConditionType.Length > 0),
            "Typed traversal finds conditions nested in native props without treating wrappers as objectives"
        );

        foreach (var type in typeof(NativeQuest).Assembly.GetTypes().Where(t => t.Namespace == typeof(NativeQuest).Namespace))
        {
            foreach (var property in type.GetProperties())
            {
                check(
                    !typeof(JToken).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(object),
                    "Concrete native field: " + type.Name + "." + property.Name
                );
            }
        }
    }
}
