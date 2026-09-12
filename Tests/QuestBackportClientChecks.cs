using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Progression;
using Path = System.IO.Path;

namespace WTT.Campaigns.Tests;

internal static class QuestBackportClientChecks
{
    internal static void Run(string gameAssembly, Action<bool, string> check)
    {
        var game = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(gameAssembly)!, "../../.."));
        var context = new ClientAssemblyContext(game, gameAssembly);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(gameAssembly));
        var status = assembly.GetType("EFT.Quests.EQuestStatus", true)!;
        var stagesType = typeof(Dictionary<,>).MakeGenericType(status, typeof(JArray));
        var names = Enum.GetNames(status);
        check(QuestBackportCompatibility.ConditionStages.Concat(QuestBackportCompatibility.RewardStages).All(names.Contains),
            "Backport stages exist in the installed client's actual quest status enum");

        // Exercise the same enum-keyed dictionary conversion that blocked /client/quest/list.
        var rejected = false;
        try
        {
            JsonConvert.DeserializeObject("{\"AutoStart\":[]}", stagesType);
        }
        catch (JsonSerializationException)
        {
            rejected = true;
        }
        check(rejected, "Installed client reproduces the captured empty AutoStart parsing failure");

        var options = new System.Text.Json.JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters())
        {
            options.Converters.Add(converter);
        }
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data/quest-backports.json")));
        foreach (var entry in manifest["Quests"]!)
        {
            var packaged = (JObject)entry["Quest"]!;
            foreach (var name in new[] { "conditions", "rewards" })
            {
                JsonConvert.DeserializeObject(packaged[name]!.ToString(), stagesType);
                check(true, "Packaged backport " + name + " parses with the installed client enum");
            }
            var legacy = (JObject)packaged.DeepClone();
            legacy["conditions"]!["AutoStart"] = new JArray();
            legacy["rewards"]!["FutureStage"] = new JArray();
            QuestBackportCompatibility.Normalize(legacy);
            check(JToken.DeepEquals(legacy, packaged), "Legacy manifest normalization preserves all objectives and rewards");
            QuestBackportCompatibility.Normalize(legacy);
            check(JToken.DeepEquals(legacy, packaged), "Repeated backport normalization is idempotent");

            var server = System.Text.Json.JsonSerializer.Deserialize<Quest>(legacy.ToString(), options)!;
            var wire = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(server, options));
            foreach (var name in new[] { "conditions", "rewards" })
            {
                JsonConvert.DeserializeObject(wire[name]!.ToString(), stagesType);
                check(true, "SPT-serialized backport " + name + " parses with the installed client enum");
            }
            foreach (var name in new[] { "conditions", "rewards" })
            {
                var unsupported = (JObject)packaged.DeepClone();
                unsupported[name]!["AutoStart"] = new JArray(new JObject { ["id"] = "unsupported-behavior" });
                var before = unsupported.ToString();
                rejected = false;
                try
                {
                    QuestBackportCompatibility.Normalize(unsupported);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                check(rejected && unsupported.ToString() == before, "Nonempty unsupported " + name + " rejected without mutation");
            }
        }
    }
}
