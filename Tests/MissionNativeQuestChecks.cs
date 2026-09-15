using Mono.Cecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class MissionNativeQuestChecks
{
    internal static void Run(string nativePath, Action<bool, string> check)
    {
        using var native = AssemblyDefinition.ReadAssembly(nativePath);
        var types = native.MainModule.GetTypes().ToDictionary(t => t.FullName);
        check(
            types.ContainsKey("EFT.Quests.ConditionGlobalVariableValue"),
            "Installed client supports mission completion variable condition"
        );
        check(
            types["EFT.Quests.ConditionGlobalVariableValue"].BaseType.FullName == "EFT.Quests.ConditionOneTarget",
            "Mission completion variable uses one target"
        );
        check(
            types["EFT.Quests.QuestTemplate/EQuestType"].Fields.Any(f => f.Name == "Exploration"),
            "Installed client accepts mission test quest type"
        );
        var world = types["EFT.GameWorld"];
        var searchController = types["EFT.ActiveSearchController"];
        var uncover = searchController.Methods.Single(m => m.Name == "UncoverContent");
        var uncoverCalls = uncover.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Select(m => m.Name).ToList();
        check(
            uncover.IsPublic
                && uncoverCalls.Contains("UncoverItem")
                && uncoverCalls.Contains("DiscoverItem")
                && uncoverCalls.Contains("UncoverContent"),
            "Native equipment initialization recursively marks containers searched and their contents known"
        );
        check(
            searchController
                .Methods.Single(m => m.IsConstructor)
                .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "UncoverContent"),
            "Preview gear uses the same search initialization as native player equipment"
        );
        var spawnCalls = world
            .Methods.Single(m => m.Name == "SpawnLootItem")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToList();
        var ownerConstructor = spawnCalls.Single(m => m.Name == ".ctor" && m.DeclaringType.FullName == "EFT.InventoryLogic.ItemController");
        check(
            ownerConstructor.Parameters.Count == 5
                && ownerConstructor.Parameters[0].ParameterType.FullName == "EFT.InventoryLogic.Item"
                && spawnCalls.IndexOf(ownerConstructor) < spawnCalls.FindIndex(m => m.Name == "CreateLootPrefab"),
            "Installed native loose loot attaches an ItemController before creating its prefab"
        );
        var ownerCalls = ownerConstructor.Resolve().Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToList();
        check(
            ownerCalls.Any(m => m.Name == "CreateItemAddress") && ownerCalls.Any(m => m.Name == "set_CurrentAddress"),
            "Native loot ownership supplies the detached root item's inventory address"
        );
        check(
            world.Methods.Any(m =>
                m.Name == "CreateStaticLoot"
                && m.Parameters.Count == 7
                && m.Parameters[0].ParameterType.FullName == "UnityEngine.GameObject"
                && m.ReturnType.FullName == "EFT.Interactive.LootItem"
            ),
            "Mission loot uses the installed native world loot constructor"
        );
        check(
            world.Methods.Any(m =>
                m.Name == "DestroyLoot" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "IKillable"
            ),
            "Mission rollback uses the native loot removal lifecycle"
        );
        var factory = types.Values.Single(t => t.Name == "ObjectsFactory");
        check(
            factory.Methods.Any(m =>
                m.Name == "CreateLootPrefab" && m.Parameters.Count == 3 && m.ReturnType.FullName == "UnityEngine.GameObject"
            ),
            "Mission loot uses an interactable native prefab"
        );
        var extraction = world.Properties.Single(p => p.Name == "ExfiltrationController").PropertyType.Resolve();
        foreach (var name in new[] { "ExfiltrationPoints", "ScavExfiltrationPoints", "SecretExfiltrationPoints" })
            check(
                extraction.Fields.Any(f => f.Name == name) || extraction.Properties.Any(p => p.Name == name),
                "Mission extraction isolation includes native " + name
            );

        string Id() => Guid.NewGuid().ToString("N")[..24];
        var layout = new MapLayout
        {
            Id = Id(),
            Name = "Test",
            Location = "Interchange",
            Start = new()
            {
                Id = Id(),
                Location = "Interchange",
                Scene = "Test",
            },
            Checkpoints =
            [
                new()
                {
                    Id = Id(),
                    Location = "Interchange",
                    Scene = "Test",
                },
            ],
            Exit = new()
            {
                Id = Id(),
                Location = "Interchange",
                Scene = "Test",
            },
        };
        var prepared = MissionTestCampaign.AddMission(
            new SeasonDefinition
            {
                Id = Id(),
                BattlePassId = Id(),
                MapLayouts = [layout],
            },
            layout.Id
        );
        var quest = prepared.Quests.Single();
        var wire = RoundTrip(quest);
        var condition = wire["conditions"]!["AvailableForFinish"]![0]!;
        check((string?)condition["conditionType"] == "GlobalVariableValue", "SPT serialization preserves mission condition type");
        check(
            (string?)condition["target"] == prepared.Story!.Variables.Single().Id,
            "SPT serialization preserves mission variable as a scalar target"
        );
        check(
            (double?)condition["value"] == 1 && (string?)condition["compareMethod"] == ">=",
            "SPT serialization preserves mission completion comparison"
        );
        var statuses = types["EFT.Quests.EQuestStatus"].Fields.Select(f => f.Name).ToHashSet();
        foreach (var section in new[] { "conditions", "rewards" })
            check(
                ((JObject)wire[section]!).Properties().All(p => statuses.Contains(p.Name)),
                "Mission " + section + " only use installed client quest-status keys"
            );
    }

    internal static JObject RoundTrip(NativeQuest quest)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters())
            options.Converters.Add(converter);
        var template = StoryQuestCompatibility.NativeTemplate(quest);
        var server = System.Text.Json.JsonSerializer.Deserialize<Quest>(JsonConvert.SerializeObject(template), options)!;
        return JObject.Parse(System.Text.Json.JsonSerializer.Serialize(server, options));
    }
}
