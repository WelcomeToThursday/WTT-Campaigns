using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class StoryEnumCompatibilityChecks
{
    // Custom bot IDs are supplied at runtime, outside the game's declared enum values.
    private enum BotRole { Assault = 1 }
    private sealed class Faction
    {
        public BotRole[] BotTypes { get; set; } = [];
    }

    internal static void Run(Action<bool, string> check)
    {
        var previous = JsonConvert.DefaultSettings;
        try
        {
            // Match UnityConverterInitializer: discovered custom converters precede built-ins.
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = { new StoryEnumConverter(), new StringEnumConverter() },
            };
            const string json = """{"blackdiv":{"BotTypes":[848420,1]}}""";
            var factions = JsonConvert.DeserializeObject<Dictionary<string, Faction>>(json)!;
            check((int)factions["blackdiv"].BotTypes[0] == 848420, "MoreBots numeric custom role survives globally discovered story converter");
            check(factions["blackdiv"].BotTypes[1] == BotRole.Assault, "Native numeric bot role still loads");
            check(JsonConvert.DeserializeObject<BotRole>("\"Assault\"") == BotRole.Assault, "Named game enum still loads");
            check((int)JsonConvert.DeserializeObject<BotRole?>("848420")! == 848420, "Nullable custom bot role still loads");
            var roundTrip = JsonConvert.DeserializeObject<Dictionary<string, Faction>>(JsonConvert.SerializeObject(factions))!;
            check((int)roundTrip["blackdiv"].BotTypes[0] == 848420, "Custom bot role round trips without remapping");
            foreach (var type in new[] { typeof(StoryActionType), typeof(StoryActionType?), typeof(StoryVariableScope), typeof(StoryVariableScope?) })
            {
                var rejected = false;
                try { JsonConvert.DeserializeObject("0", type); }
                catch (JsonSerializationException) { rejected = true; }
                check(rejected, "Story numeric ordinals remain rejected: " + type.Name);
            }
            check(JsonConvert.DeserializeObject<StoryActionType>("\"AcceptQuest\"") == StoryActionType.AcceptQuest, "Named story actions still load");
            check(JsonConvert.DeserializeObject<StoryVariableScope>("\"Profile\"") == StoryVariableScope.Profile, "Named story scopes still load");
            check(JsonConvert.DeserializeObject<StoryVariableScope?>("null") == null, "Nullable story scope retains null");
        }
        finally
        {
            JsonConvert.DefaultSettings = previous;
        }
    }
}
