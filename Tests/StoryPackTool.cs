using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Tests;

internal static class StoryPackTool
{
    internal static void Compose(string input, string overlayPath, string output)
    {
        output = Path.GetFullPath(output);
        if (File.Exists(output))
        {
            throw new IOException("Choose a new output filename; existing packs are preserved.");
        }
        var workspace = Path.Combine(Path.GetTempPath(), "WTT-StoryPack-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        foreach (var source in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(workspace, "data", Path.GetRelativePath(data, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        var repository = new SeasonRepository(workspace);
        var draft = repository.Import(File.ReadAllBytes(input));
        var overlay = JObject.Parse(File.ReadAllText(overlayPath));
        if (overlay.Properties().Any(p => p.Name is not ("Story" or "Quests" or "Locales" or "Dependencies")))
        {
            throw new InvalidDataException("A story overlay may contain only Story, Quests, Locales and Dependencies.");
        }
        draft.Definition.Story = overlay["Story"]?.ToObject<StoryDefinition>() ?? throw new InvalidDataException("Story is required.");
        foreach (var quest in overlay["Quests"] as JArray ?? new JArray())
        {
            if (draft.Definition.Quests.Any(q => q.Id == (string?)quest["_id"]))
            {
                throw new InvalidDataException("An added story quest collides with a base-pack quest.");
            }
            draft.Definition.Quests.Add(quest.ToObject<NativeQuest>()!);
        }
        foreach (var language in (overlay["Locales"] as JObject ?? new JObject()).Properties())
        {
            if (!draft.Definition.Locales.TryGetValue(language.Name, out var texts))
            {
                texts = new();
                draft.Definition.Locales[language.Name] = texts;
            }
            foreach (var text in ((JObject)language.Value).Properties())
            {
                texts[text.Name] = (string)text.Value!;
            }
        }
        foreach (var dependency in (overlay["Dependencies"] as JArray ?? new JArray()).Values<string>())
        {
            if (!draft.Definition.Dependencies.Contains(dependency!))
            {
                draft.Definition.Dependencies.Add(dependency!);
            }
        }
        draft = repository.Save(draft);
        var validation = SeasonValidator.Validate(draft.Definition);
        if (!validation.CanPublish)
        {
            throw new InvalidDataException(JsonConvert.SerializeObject(validation, Formatting.Indented));
        }
        var key = repository.Publish(draft, validation);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var bytes = repository.Export(key);
        // Re-import the exported bytes through the production checksum and identity checks.
        var roundtrip = repository.Import(bytes);
        if (SeasonRepository.GameplayHash(roundtrip.Definition) != SeasonRepository.GameplayHash(draft.Definition))
        {
            throw new InvalidDataException("The exported story pack changed its gameplay identity.");
        }
        File.WriteAllBytes(output, bytes);
        Console.WriteLine("Validated story pack: " + output);
        Console.WriteLine("Authoring recovery workspace: " + workspace);
    }
}
