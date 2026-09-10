using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Hub;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class CreatorChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtt-creator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(directory, "data", Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "data"), file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var store = new SeasonRepository(directory);
            StoryImageRouteChecks.Run(store, directory, check);
            RaidAuthoringChecks.Run(store, check);
            var first = store.Create(false);
            check(
                first.Definition.Id != store.Legacy.Id && first.Definition.BattlePassId != store.Legacy.BattlePassId,
                "Blank campaign has independent identities"
            );
            check(SeasonValidator.Validate(first.Definition).CanPublish, "Blank campaign is structurally valid");
            check(
                !JObject
                    .FromObject(first.Definition)
                    .Descendants()
                    .OfType<JProperty>()
                    .Any(p => p.Name is "Claimed" or "Revision" && p.Parent != null && p.Parent.Parent != null),
                "Authored rewards exclude profile state"
            );
            var old = SeasonCompiler.Copy(first);
            first.Definition.Name = "Test campaign";
            first = store.Save(first);
            Reject(() => store.Save(old), "Concurrent draft save is rejected");
            check(store.Load(first.Id).Definition.Name == "Test campaign", "Save conflict preserves newer draft");
            var state = new HubProgress();
            var raid = new HubRaid
            {
                Spawned = new() { ["a"] = "doc", ["b"] = "doc" },
            };
            check(
                HubRules.Pickup(state, raid, "a", 100, 1, 60) && !HubRules.Pickup(state, raid, "b", 101, 1, 60),
                "Configured allowance enforced"
            );
            check(HubRules.Remaining(state, 160, 1, 60) == 1, "Configured allowance reset");
            var invalid = SeasonCompiler.Copy(first.Definition);
            invalid.Pages[0].PreviousRequirement = 1;
            check(!SeasonValidator.Validate(invalid).CanPublish, "Impossible page gate rejected");
            invalid = SeasonCompiler.Copy(first.Definition);
            invalid.Documents.Add(SeasonCompiler.Copy(invalid.Documents[0]));
            check(!SeasonValidator.Validate(invalid).CanPublish, "Duplicate document identities rejected");
            invalid = SeasonCompiler.Copy(first.Definition);
            invalid.Collection.ClassifiedChancePercent = 101;
            check(!SeasonValidator.Validate(invalid).CanPublish, "Invalid collection chance rejected");
            invalid = SeasonCompiler.Copy(first.Definition);
            invalid
                .Pages[0]
                .Rewards.Add(
                    new()
                    {
                        Id = SeasonRepository.NewId(),
                        X = 1,
                        Width = 2,
                    }
                );
            check(!SeasonValidator.Validate(invalid).CanPublish, "Out-of-grid reward rejected");
            invalid = SeasonCompiler.Copy(first.Definition);
            invalid.Perks.Personal.Add(new() { Id = SeasonRepository.NewId(), Effects = [new() { EffectId = "lucky" }] });
            check(!SeasonValidator.Validate(invalid).CanPublish, "Unsupported authored effects rejected");
            invalid = SeasonCompiler.Copy(first.Definition);
            invalid.Items[0].CloneFrom = invalid.Items[0].Id;
            check(!SeasonValidator.Validate(invalid).CanPublish, "Cyclic clone dependencies rejected");
            var extension = JObject.FromObject(first.Definition);
            extension["Starting"]!["futureOption"] = new JObject { ["enabled"] = false };
            check(
                JObject.FromObject(extension.ToObject<SeasonDefinition>()!)["Starting"]?["futureOption"] != null,
                "Unknown nested imported fields round trip"
            );
            var translation = SeasonCompiler.Copy(first.Definition);
            translation.Locales["fr"] = new() { [translation.Id + " name"] = "Nouvelle saison" };
            check(
                SeasonCompiler.Texts(translation, "fr")[translation.Id + " name"] == "Nouvelle saison"
                    && SeasonCompiler.Texts(translation, "fr")[translation.Documents[0].Id + " name"] == translation.Documents[0].Name,
                "Translations fall back to canonical English text"
            );
            var bytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="
            );
            var asset = store.AddImage(bytes);
            Reject(() => store.AddImage(bytes.Take(24).ToArray()), "Truncated PNG rejected");
            var corruptImage = bytes.ToArray();
            corruptImage[45] ^= 1;
            Reject(() => store.AddImage(corruptImage), "Corrupt PNG checksum rejected");
            first.Definition.UniversalImage = first.Definition.UniversalUnavailableImage = asset;
            first.Definition.Documents[0].Image = first.Definition.Documents[0].UnavailableImage = asset;
            first = store.Save(first);
            var key = store.Publish(first, SeasonValidator.Validate(first.Definition));
            var exported = store.Export(key);
            DraftManagementChecks.Run(store, directory, first, check);
            var imported = store.Import(exported);
            check(
                imported.Definition.Id == first.Definition.Id && imported.Definition.Revision == 1,
                "Pack round trip preserves identity and revision"
            );
            check(
                SeasonRepository.GameplayHash(imported.Definition) == SeasonRepository.GameplayHash(first.Definition),
                "Pack round trip preserves gameplay hash"
            );
            store.MarkUsed(first.Definition);
            var cosmetic = SeasonCompiler.Copy(first.Definition);
            cosmetic.Name = "Corrected name";
            cosmetic.Branding.Badge = asset;
            store.CheckGameplay(cosmetic);
            check(true, "Cosmetic updates preserve used-campaign gameplay");
            cosmetic.Collection.DocumentLimit++;
            Reject(() => store.CheckGameplay(cosmetic), "Used-campaign gameplay changes rejected");
            store.Queue(key);
            check(
                store.Current.Definition.Id == store.Legacy.Id && store.Selection.Pending == key,
                "Pending activation leaves running campaign unchanged"
            );
            store.Activate(key);
            check(store.Current.Definition.Id == first.Definition.Id, "Activation selects published pack");
            var copy = store.Current.Definition;
            copy.Name = "Mutated";
            check(store.Current.Definition.Name != "Mutated", "Runtime definition is isolated from callers");
            var reload = new SeasonRepository(directory);
            check(reload.Selection.Active == key, "Active selection persists across repository restart");
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("../escape.json").Open());
                writer.Write("{}");
            }
            Reject(() => store.Import(memory.ToArray()), "Archive traversal rejected");
            Reject(() => store.Load("../selection"), "Draft path traversal rejected");
            File.AppendAllText(Path.Combine(directory, "creator", "packs", key, "definition.json"), " ");
            Reject(() => store.Pack(key), "Pack checksum corruption rejected");
            var corruptDraftPath = Path.Combine(directory, "creator", "drafts", first.Id + ".json");
            File.WriteAllText(corruptDraftPath, "{");
            check(store.Load(first.Id).Revision == first.Revision - 1, "Corrupt draft recovers previous atomic backup");
            var duplicate = SeasonRepository.Duplicate(store.Legacy);
            check(
                !duplicate.Perks.All.Select(p => p.Id).Intersect(store.Legacy.Perks.All.Select(p => p.Id)).Any(),
                "Duplication replaces owned perk identities"
            );
            check(
                duplicate.Perks.All.SelectMany(p => p.Conflicts).All(id => duplicate.Perks.All.Any(p => p.Id == id)),
                "Duplication repairs perk conflicts"
            );
            check(
                duplicate.AllRewards.SelectMany(r => r.Costs).All(c => duplicate.Documents.Any(d => d.Id == c.DocumentId)),
                "Duplication repairs document costs"
            );
            void Reject(Action action, string name)
            {
                try
                {
                    action();
                }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException)
                {
                    check(true, name);
                    return;
                }
                check(false, name);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
