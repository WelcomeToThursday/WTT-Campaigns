using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Configuration;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Seasons;
using SPTarkov.DI.Annotations;

namespace SeasonalPerks.Server.Seasons;

public sealed class SeasonRuntimeSnapshot
{
    private readonly string _definition;

    public SeasonRuntimeSnapshot(SeasonDefinition definition)
    {
        _definition = JsonConvert.SerializeObject(definition);
    }

    public SeasonDefinition Definition
    {
        get { return JsonConvert.DeserializeObject<SeasonDefinition>(_definition)!; }
    }

    public HubState Hub
    {
        get { return SeasonCompiler.Hub(Definition); }
    }

    public JObject Gameplay
    {
        get { return SeasonCompiler.Gameplay(Definition); }
    }
}

public sealed class SeasonSelection
{
    public string Active { get; set; } = "legacy";
    public string? Pending { get; set; }
    public string Error { get; set; } = "";
}

public sealed class DraftEnvelope
{
    public string Id { get; set; } = "";
    public long Revision { get; set; }
    public SeasonDefinition Definition { get; set; } = new();
}

[Injectable(InjectionType.Singleton)]
public sealed class SeasonRepository
{
    private readonly object _gate = new();
    private readonly string _root;
    public const string LegacyId = "69e232a764dfe95549003f0f";
    public SeasonRuntimeSnapshot Current { get; private set; }
    public SeasonSelection Selection { get; private set; }
    public SeasonDefinition Legacy { get; }

    public SeasonRepository()
        : this(Metadata.DirectoryPath) { }

    // Also permits isolated filesystem tests without loading an SPT host.
    internal SeasonRepository(string modDirectory)
    {
        ModDirectory = Path.GetFullPath(modDirectory);
        _root = Path.Combine(ModDirectory, "creator");
        foreach (var folder in new[] { "drafts", "packs", "assets", "used" })
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
        }

        var legacyPath = Path.Combine(_root, "legacy.json");
        Legacy = File.Exists(legacyPath) ? Read<SeasonDefinition>(legacyPath) : ReadLegacy();
        if (!File.Exists(legacyPath))
        {
            Atomic(legacyPath, JsonConvert.SerializeObject(Legacy, Formatting.Indented));
        }

        Current = new(Legacy);
        try
        {
            Selection = File.Exists(SelectionPath) ? Read<SeasonSelection>(SelectionPath) : new();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Selection = new() { Error = "Selection could not be recovered: " + e.Message };
        }
    }

    public string ModDirectory { get; }
    private string SelectionPath
    {
        get { return Path.Combine(_root, "selection.json"); }
    }

    public static string NewId()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
    }

    public static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string GameplayHash(SeasonDefinition definition)
    {
        return Hash(
            Encoding.UTF8.GetBytes(SeasonCompiler.Canonical(SeasonCompiler.GameplayIdentity(definition)).ToString(Formatting.None))
        );
    }

    private static T Read<T>(string path)
    {
        T Parse(string file)
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file))
                ?? throw new InvalidDataException("Invalid file: " + Path.GetFileName(file));
        }

        try
        {
            return Parse(path);
        }
        catch (Exception e) when ((e is JsonException or InvalidDataException or IOException) && File.Exists(path + ".bak"))
        {
            return Parse(path + ".bak");
        }
    }

    public bool IsUsed(string id)
    {
        return File.Exists(Path.Combine(_root, "used", CheckId(id) + ".json"));
    }

    public List<string> StorageWarnings { get; } = new();

    private T? TryRead<T>(string path)
        where T : class
    {
        try
        {
            return Read<T>(path);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException)
        {
            var warning = Path.GetFileName(path) + ": " + e.Message;
            if (!StorageWarnings.Contains(warning))
            {
                StorageWarnings.Add(warning);
            }
            return null;
        }
    }

    public static void Atomic(string path, string text)
    {
        AtomicBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private static void AtomicBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + NewId() + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, path + ".bak");
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static string CheckId(string id)
    {
        return SeasonValidator.IsId(id) ? id : throw new InvalidDataException("Invalid identifier.");
    }

    private string DraftPath(string id)
    {
        return Path.Combine(_root, "drafts", CheckId(id) + ".json");
    }

    public List<DraftEnvelope> Drafts()
    {
        lock (_gate)
        {
            return Directory
                .GetFiles(Path.Combine(_root, "drafts"), "*.json")
                .Select(TryRead<DraftEnvelope>)
                .OfType<DraftEnvelope>()
                .OrderBy(d => d.Definition.Name)
                .ToList();
        }
    }

    public List<(string Key, SeasonManifest Manifest)> Packs()
    {
        lock (_gate)
        {
            return Directory
                .GetDirectories(Path.Combine(_root, "packs"))
                .Where(p => File.Exists(Path.Combine(p, "manifest.json")))
                .Select(p => (Path.GetFileName(p), TryRead<SeasonManifest>(Path.Combine(p, "manifest.json"))))
                .Where(p => p.Item2 != null)
                .Select(p => (p.Item1, p.Item2!))
                .OrderBy(p => p.Item2.Name)
                .ToList();
        }
    }

    public DraftEnvelope Load(string id)
    {
        lock (_gate)
        {
            return Read<DraftEnvelope>(DraftPath(id));
        }
    }

    public DraftEnvelope Save(DraftEnvelope draft)
    {
        lock (_gate)
        {
            var path = DraftPath(draft.Id);
            var revision = File.Exists(path) ? Read<DraftEnvelope>(path).Revision : 0;
            if (revision != draft.Revision)
            {
                throw new InvalidOperationException("This draft changed in another tab. Reload before saving.");
            }

            var next = SeasonCompiler.Copy(draft);
            next.Revision++;
            Atomic(path, JsonConvert.SerializeObject(next, Formatting.Indented));
            return next;
        }
    }

    public DraftEnvelope Create(bool duplicate, SeasonDefinition? source = null)
    {
        source ??= duplicate ? Current.Definition : Legacy;
        var definition = Duplicate(source);
        if (!duplicate)
        {
            var document = definition.Documents.First();
            definition.Name = "New season";
            definition.Description = "";
            definition.Author = "";
            definition.Perks = new();
            definition.Rules = new();
            definition.Pages = new() { new() };
            definition.SeasonalRewards.Clear();
            definition.Quests = new();
            definition.Offers = new();
            definition.Crates.Clear();
            definition.ExchangeCrate = "";
            definition.Documents = new() { document };
            definition.Slides.Clear();
            definition.Branding = new();
            definition.Starting = new();
            // A blank season can reuse the installed document model without owning all legacy items.
            var original = (source ?? Current.Definition).Documents.First();
            definition.ImportedItems = new();
            definition.Items = new()
            {
                new()
                {
                    Id = document.ItemId,
                    CloneFrom = original.ItemId,
                    Name = "Season document",
                },
            };
            definition.Locales = new() { ["en"] = new() };
        }
        return Save(new DraftEnvelope { Id = NewId(), Definition = definition });
    }

    public static SeasonDefinition Duplicate(SeasonDefinition source)
    {
        var owned = new HashSet<string>(
            source
                .Perks.All.Select(p => p.Id)
                .Concat(source.Documents.Select(d => d.Id))
                .Concat(source.Items.Select(i => i.Id))
                .Concat(source.ImportedItems.Properties().Select(p => p.Name))
                .Concat(source.AllRewards.Select(r => r.Id))
                .Concat(source.Quests.Select(q => (string)q["_id"]!))
        )
        {
            source.Id,
            source.BattlePassId,
        };
        foreach (
            var node in source
                .Quests.Descendants()
                .Concat(
                    source.AllRewards.SelectMany(r =>
                        r.Grants.OfType<JObject>().Where(g => (string?)g["type"] != "AssortmentUnlock").SelectMany(g => g.Descendants())
                    )
                )
                .OfType<JObject>()
        )
        {
            foreach (var field in new[] { "id", "_id" })
            {
                if ((string?)node[field] is { } id && SeasonValidator.IsId(id))
                {
                    owned.Add(id);
                }
            }
        }

        var replacements = owned.ToDictionary(id => id, _ => NewId());
        JToken Rewrite(JToken token)
        {
            if (token is JObject obj)
            {
                return new JObject(obj.Properties().Select(p => new JProperty(Replace(p.Name), Rewrite(p.Value))));
            }

            if (token is JArray array)
            {
                return new JArray(array.Select(Rewrite));
            }

            return token.Type == JTokenType.String ? new JValue(Replace((string)token!)) : token.DeepClone();
        }
        string Replace(string text)
        {
            if (replacements.TryGetValue(text, out var replacement))
            {
                return replacement;
            }
            // Locale keys are identity + suffix, not arbitrary prose substitutions.
            var space = text.IndexOf(' ');
            return space == 24 && replacements.TryGetValue(text.Substring(0, 24), out replacement)
                ? replacement + text.Substring(24)
                : text;
        }
        var copy = Rewrite(JObject.FromObject(source)).ToObject<SeasonDefinition>()!;
        copy.Name = source.Name + " copy";
        copy.Revision = 0;
        copy.Version = "1.0.0";
        copy.Legacy = false;
        foreach (var perk in copy.Perks.All.Where(p => EffectSupport.UnavailableReason(p) != null))
        {
            perk.Enabled = false;
            copy.Rules.EnabledCommonIds.Remove(perk.Id);
        }
        foreach (var quest in copy.Quests.OfType<JObject>())
        {
            if (
                quest
                    .Descendants()
                    .OfType<JObject>()
                    .Any(c =>
                        c["conditionType"] != null
                        && (string?)c["conditionType"] is not ("Quest" or "Level" or "TraderLoyalty" or "FindItem" or "HandoverItem")
                    )
            )
            {
                quest["_seasonalEnabled"] = false;
            }
        }

        return copy;
    }

    public void MarkUsed(SeasonDefinition definition)
    {
        lock (_gate)
        {
            CheckGameplay(definition);
            var path = Path.Combine(_root, "used", CheckId(definition.Id) + ".json");
            if (!File.Exists(path))
            {
                Atomic(path, JsonConvert.SerializeObject(new { Hash = GameplayHash(definition) }));
            }
        }
    }

    public void CheckGameplay(SeasonDefinition definition)
    {
        var path = Path.Combine(_root, "used", CheckId(definition.Id) + ".json");
        if (File.Exists(path) && (string?)JObject.Parse(File.ReadAllText(path))["Hash"] != GameplayHash(definition))
        {
            throw new InvalidOperationException("This season has been used. Duplicate it as a new season to change gameplay.");
        }
    }

    public string Publish(DraftEnvelope draft, SeasonValidationResult validation)
    {
        lock (_gate)
        {
            if (draft.Definition.Legacy)
            {
                throw new InvalidOperationException("Duplicate the built-in season before publishing changes.");
            }

            if (!validation.CanPublish || !SeasonValidator.Validate(draft.Definition).CanPublish)
            {
                throw new InvalidOperationException("Fix validation errors before publishing.");
            }

            var saved = Load(draft.Id);
            if (
                saved.Revision != draft.Revision
                || JsonConvert.SerializeObject(saved.Definition) != JsonConvert.SerializeObject(draft.Definition)
            )
            {
                throw new InvalidOperationException("Save your latest changes before publishing.");
            }

            var definition = SeasonCompiler.Copy(draft.Definition);
            CheckGameplay(definition);
            definition.Revision =
                Packs().Where(p => p.Manifest.SeasonId == definition.Id).Select(p => p.Manifest.Revision).DefaultIfEmpty(0).Max() + 1;
            var key = NewId();
            var folder = Path.Combine(_root, "packs", key);
            Directory.CreateDirectory(folder);
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(definition, Formatting.Indented));
            var manifest = new SeasonManifest
            {
                SeasonId = definition.Id,
                BattlePassId = definition.BattlePassId,
                Name = definition.Name,
                Version = definition.Version,
                Revision = definition.Revision,
                GameplayHash = GameplayHash(definition),
                Dependencies = SeasonCompiler.Dependencies(definition).ToList(),
            };
            File.WriteAllBytes(Path.Combine(folder, "definition.json"), bytes);
            manifest.Files["definition.json"] = Hash(bytes);
            foreach (var asset in SeasonCompiler.Assets(definition).Where(SeasonValidator.IsId))
            {
                var source = AssetPath(asset) ?? throw new InvalidOperationException("Missing artwork: " + asset);
                var relative = "assets/" + asset + ".png";
                Directory.CreateDirectory(Path.Combine(folder, "assets"));
                File.Copy(source, Path.Combine(folder, relative));
                manifest.Files[relative] = Hash(File.ReadAllBytes(source));
            }
            // Write the manifest last: incomplete publication is never listed as a pack.
            Atomic(Path.Combine(folder, "manifest.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return key;
        }
    }

    public SeasonDefinition Pack(string key)
    {
        if (key == "legacy")
        {
            return SeasonCompiler.Copy(Legacy);
        }

        var folder = Path.Combine(_root, "packs", CheckId(key));
        var manifest = Read<SeasonManifest>(Path.Combine(folder, "manifest.json"));
        if (manifest.FormatVersion != 1 || manifest.ProtocolVersion != 2 || !manifest.Files.ContainsKey("definition.json"))
        {
            throw new InvalidDataException("Incompatible pack manifest.");
        }

        foreach (var file in manifest.Files)
        {
            if (!PackFile(file.Key))
            {
                throw new InvalidDataException("Invalid pack path.");
            }

            if (Hash(File.ReadAllBytes(Path.Combine(folder, file.Key))) != file.Value)
            {
                throw new InvalidDataException("Pack checksum mismatch: " + file.Key);
            }
        }
        var definition = Read<SeasonDefinition>(Path.Combine(folder, "definition.json"));
        if (
            definition.Id != manifest.SeasonId
            || definition.BattlePassId != manifest.BattlePassId
            || definition.Revision != manifest.Revision
            || GameplayHash(definition) != manifest.GameplayHash
        )
        {
            throw new InvalidDataException("Pack identity mismatch.");
        }

        return definition;
    }

    public void Queue(string key)
    {
        lock (_gate)
        {
            CheckGameplay(Pack(key));
            var next = SeasonCompiler.Copy(Selection);
            next.Pending = key;
            next.Error = "";
            Atomic(SelectionPath, JsonConvert.SerializeObject(next, Formatting.Indented));
            Selection = next;
        }
    }

    public void Activate(string key)
    {
        lock (_gate)
        {
            var snapshot = new SeasonRuntimeSnapshot(Pack(key));
            var next = new SeasonSelection { Active = key };
            Atomic(SelectionPath, JsonConvert.SerializeObject(next, Formatting.Indented));
            Current = snapshot;
            Selection = next;
        }
    }

    public void ActivationFailed(string reason)
    {
        lock (_gate)
        {
            Selection.Pending = null;
            Selection.Error = reason;
            SaveSelection();
        }
    }

    private void SaveSelection()
    {
        Atomic(SelectionPath, JsonConvert.SerializeObject(Selection, Formatting.Indented));
    }

    public IEnumerable<string> Artwork()
    {
        return SeasonCompiler
            .Assets(Legacy)
            .Concat(Directory.GetFiles(Path.Combine(_root, "assets"), "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>())
            .Where(SeasonValidator.IsId)
            .Distinct();
    }

    public string ArtworkName(string id)
    {
        var perk = Legacy.Perks.All.FirstOrDefault(p => p.ImageUrl == id);
        if (perk != null)
        {
            return Legacy.Locales["en"].GetValueOrDefault(perk.Id + " name", "Perk") + " icon";
        }

        var document = Legacy.Documents.FirstOrDefault(d => d.Image == id || d.UnavailableImage == id);
        if (document != null)
        {
            return document.Name + (document.Image == id ? " document" : " unavailable");
        }

        var reward = Legacy.AllRewards.FirstOrDefault(r => r.Image == id || r.BigImage == id);
        return reward?.Name ?? "Uploaded artwork " + id.Substring(0, 8);
    }

    public string? AssetPath(string id)
    {
        if (!SeasonValidator.IsId(id))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(_root, "assets", id + ".png"),
            Path.Combine(ModDirectory, "hub-images", id + ".png"),
            Path.Combine(ModDirectory, "icons", id + ".png"),
        }.Concat(Directory.GetDirectories(Path.Combine(_root, "packs")).Select(p => Path.Combine(p, "assets", id + ".png")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public string AddImage(byte[] bytes)
    {
        if (bytes.Length is < 24 or > 8388608 || !bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidDataException("Choose a PNG under 8 MB.");
        }

        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
        {
            throw new InvalidDataException("Artwork must be at most 4096 × 4096.");
        }

        var offset = 8;
        var header = false;
        var data = false;
        var ended = false;
        while (offset <= bytes.Length - 12)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length < 0 || (long)offset + length + 12 > bytes.Length)
            {
                throw new InvalidDataException("Truncated PNG chunk.");
            }

            var kind = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            uint crc = 0xffffffff;
            for (var index = offset + 4; index < offset + 8 + length; index++)
            {
                crc ^= bytes[index];
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
                }
            }
            if (~crc != System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + length + 8, 4)))
            {
                throw new InvalidDataException("Corrupt PNG checksum.");
            }

            if (!header && (kind != "IHDR" || length != 13))
            {
                throw new InvalidDataException("PNG header is missing.");
            }

            if (kind == "IHDR")
            {
                if (header)
                {
                    throw new InvalidDataException("Duplicate PNG header.");
                }
                header = true;
            }
            if (kind == "IDAT")
            {
                data = true;
            }

            offset += length + 12;
            if (kind == "IEND")
            {
                ended = length == 0;
                break;
            }
        }
        if (!header || !data || !ended || offset != bytes.Length)
        {
            throw new InvalidDataException("Incomplete PNG image.");
        }

        var id = Hash(bytes).Substring(0, 24);
        lock (_gate)
        {
            var path = Path.Combine(_root, "assets", id + ".png");
            if (!File.Exists(path))
            {
                AtomicBytes(path, bytes);
            }
        }
        return id;
    }

    public byte[] Export(string key)
    {
        _ = Pack(key);
        if (key == "legacy")
        {
            throw new InvalidOperationException("Create and publish a draft before exporting.");
        }

        var folder = Path.Combine(_root, "packs", CheckId(key));
        var manifest = Read<SeasonManifest>(Path.Combine(folder, "manifest.json"));
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var file in manifest.Files.Keys.Append("manifest.json"))
            {
                zip.CreateEntryFromFile(Path.Combine(folder, file), file);
            }
        }

        return memory.ToArray();
    }

    private static bool PackFile(string name)
    {
        return name == "definition.json"
            || (
                name.StartsWith("assets/", StringComparison.Ordinal)
                && name.EndsWith(".png", StringComparison.Ordinal)
                && SeasonValidator.IsId(name.Substring(7, name.Length - 11))
            );
    }

    public DraftEnvelope Import(byte[] bytes)
    {
        if (bytes.Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("Pack exceeds 64 MB.");
        }

        using var memory = new MemoryStream(bytes);
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        if (
            zip.Entries.Count > 2048
            || zip.Entries.Sum(e => e.Length) > 128 * 1024 * 1024
            || zip.Entries.Any(e => e.Length > 16 * 1024 * 1024)
            || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count
        )
        {
            throw new InvalidDataException("Pack exceeds extraction limits or has duplicate paths.");
        }

        if (zip.Entries.Any(e => e.FullName != "manifest.json" && !PackFile(e.FullName)))
        {
            throw new InvalidDataException("Unexpected file in season pack.");
        }

        var extracted = new Dictionary<string, byte[]>();
        long expanded = 0;
        byte[] Entry(string name)
        {
            if (extracted.TryGetValue(name, out var cached))
            {
                return cached;
            }
            using var entry = (zip.GetEntry(name) ?? throw new InvalidDataException("Missing " + name)).Open();
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            int count;
            while ((count = entry.Read(buffer)) > 0)
            {
                expanded += count;
                if (output.Length + count > 16 * 1024 * 1024 || expanded > 128 * 1024 * 1024)
                {
                    throw new InvalidDataException("Pack exceeds extraction limits.");
                }
                output.Write(buffer, 0, count);
            }
            return extracted[name] = output.ToArray();
        }
        var manifest = JsonConvert.DeserializeObject<SeasonManifest>(Encoding.UTF8.GetString(Entry("manifest.json")))!;
        if (
            manifest.FormatVersion != 1
            || manifest.ProtocolVersion != 2
            || !manifest.Files.ContainsKey("definition.json")
            || manifest.Files.Count != zip.Entries.Count - 1
        )
        {
            throw new InvalidDataException("Incompatible or incomplete manifest.");
        }

        foreach (var file in manifest.Files)
        {
            if (!PackFile(file.Key) || Hash(Entry(file.Key)) != file.Value)
            {
                throw new InvalidDataException("Pack checksum mismatch.");
            }
        }

        var definition = JsonConvert.DeserializeObject<SeasonDefinition>(Encoding.UTF8.GetString(Entry("definition.json")))!;
        if (
            definition.Id != manifest.SeasonId
            || definition.BattlePassId != manifest.BattlePassId
            || definition.Revision != manifest.Revision
            || GameplayHash(definition) != manifest.GameplayHash
        )
        {
            throw new InvalidDataException("Pack identity mismatch.");
        }

        if (definition.Legacy)
        {
            throw new InvalidDataException("The built-in compatibility flag cannot be imported.");
        }

        foreach (var file in manifest.Files.Keys.Where(p => p.StartsWith("assets/", StringComparison.Ordinal)))
        {
            var assetBytes = Entry(file);
            var id = Path.GetFileNameWithoutExtension(file);
            var existing = AssetPath(id);
            if (existing != null && Hash(File.ReadAllBytes(existing)) != Hash(assetBytes))
            {
                throw new InvalidDataException("Artwork identity collision.");
            }

            _ = AddImage(assetBytes);
            var path = Path.Combine(_root, "assets", id + ".png");
            if (!File.Exists(path))
            {
                AtomicBytes(path, assetBytes);
            }
        }
        return Save(new DraftEnvelope { Id = NewId(), Definition = definition });
    }

    private SeasonDefinition ReadLegacy()
    {
        T Data<T>(string name)
        {
            return Read<T>(Path.Combine(ModDirectory, "data", name));
        }

        var hub = Data<HubState>("hub.json");
        var gameplay = Data<JObject>("hub-gameplay.json");
        var perks = Data<Catalogue>("catalogue.json");
        foreach (var p in perks.All)
        {
            p.ImageUrl = Path.GetFileNameWithoutExtension(p.ImageUrl);
        }

        SeasonReward Reward(HubReward tile)
        {
            var node = JObject.FromObject(tile);
            foreach (var field in new[] { "Claimed", "CanClaim", "Eligibility", "UnavailableReason", "UniversalNeeded" })
            {
                node.Remove(field);
            }

            var reward = node.ToObject<SeasonReward>()!;
            reward.Grants = (JArray)gameplay["Rewards"]![tile.Id]!["Grants"]!.DeepClone();
            reward.Conditions = (JArray)gameplay["Rewards"]![tile.Id]!["Conditions"]!.DeepClone();
            return reward;
        }
        var definition = new SeasonDefinition
        {
            Id = hub.SeasonId,
            BattlePassId = hub.Id,
            Name = "Season One",
            Legacy = true,
            Perks = perks,
            Pages = hub
                .Pages.Select(p => new SeasonPage
                {
                    PreviousRequirement = p.PreviousRequirement,
                    Rewards = p.Rewards.Select(Reward).ToList(),
                })
                .ToList(),
            SeasonalRewards = hub.SeasonalRewards.Select(Reward).ToList(),
            Documents = hub
                .Documents.Select(d => new SeasonDocument
                {
                    Id = d.Id,
                    Name = d.Name,
                    Image = d.Image,
                    UnavailableImage = d.UnavailableImage,
                    ItemId = (string)gameplay["Documents"]!.First(x => (string?)x["id"] == d.Id)["itemId"]!,
                })
                .ToList(),
            Slides = hub.Slides.ToList(),
            UniversalImage = hub.UniversalImage,
            UniversalUnavailableImage = hub.UniversalUnavailableImage,
            ExchangeRate = (int)gameplay["ExchangeRate"]!,
            ExchangeCrate = (string)gameplay["ItemExchange"]!["itemId"]!,
            CrateCost = (int)gameplay["ItemExchange"]!["requiredDocuments"]!,
            Quests = Data<JArray>("hub-quests.json"),
            Offers = (JArray?)gameplay["Offers"]?.DeepClone() ?? new(),
            ImportedItems = Data<JObject>("season-items.json"),
            Rules = File.Exists(Path.Combine(ModDirectory, "config.json"))
                ? Read<Rules>(Path.Combine(ModDirectory, "config.json"))
                : new Rules
                {
                    EnabledCommonIds = perks.Common.Where(p => EffectSupport.UnavailableReason(p) == null).Select(p => p.Id).ToList(),
                },
        };
        definition.Locales["en"] = Data<Dictionary<string, string>>("locales/en.json");
        foreach (var pair in Data<Dictionary<string, string>>("locales/season-items-en.json"))
        {
            definition.Locales["en"][pair.Key] = pair.Value;
        }

        if (File.Exists(Path.Combine(ModDirectory, "hub-config.json")))
        {
            var config = Read<JObject>(Path.Combine(ModDirectory, "hub-config.json"));
            JsonConvert.PopulateObject(config.ToString(), definition.Collection);
        }
        return definition;
    }
}
