using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace WTT.Campaigns.Server.Profiles;

/// <summary>Routes character files separately from launcher accounts, retaining SPT's native save pipeline.</summary>
[Injectable(InjectionType.Singleton)]
public sealed class SeasonProfileStorage
{
    private static readonly ConcurrentDictionary<string, byte> Characters = new(StringComparer.Ordinal);
    public static string DirectoryPath
    {
        get { return Path.GetFullPath("user/seasonal/profiles"); }
    }

    public static bool Contains(string id)
    {
        return Characters.ContainsKey(id);
    }

    public static void Register(string id)
    {
        if (!MongoId.IsValidMongoId(id))
        {
            throw new InvalidDataException("Invalid campaign profile identity.");
        }

        Characters.TryAdd(id, 0);
        Directory.CreateDirectory(DirectoryPath);
    }

    public async Task Initialize(SaveServer saves)
    {
        Directory.CreateDirectory(DirectoryPath);
        var links = Path.GetFullPath("user/profileData");
        if (Directory.Exists(links))
        {
            foreach (var folder in Directory.EnumerateDirectories(links))
            {
                var root = Path.GetFileName(folder);
                var path = Path.Combine(folder, "wttCampaignsAccount.json");
                if (!MongoId.IsValidMongoId(root) || !File.Exists(path))
                {
                    continue;
                }

                var link =
                    JsonConvert.DeserializeObject<AccountLink>(File.ReadAllText(path))
                    ?? throw new InvalidDataException("Invalid campaign account link: " + root);
                var ids = link
                    .Characters.Select(c => c.ProfileId)
                    .Concat(link.Seasons.Values.Select(c => c.ProfileId))
                    .Concat(link.RetiredCharacters.Keys)
                    .Concat(link.SeasonalId == null ? [] : new[] { link.SeasonalId })
                    .Distinct();
                foreach (var id in ids)
                {
                    if (id == root)
                    {
                        throw new InvalidDataException("A campaign character cannot own its launcher account.");
                    }

                    Register(id);
                }
            }
        }
        // Persisted ownership also recovers a character whose account-link update was interrupted.
        foreach (var profile in saves.GetProfiles().Values)
        {
            var pmc = profile.CharacterData?.PmcData;
            if (pmc == null)
            {
                continue;
            }

            var owner = SeasonService.State(pmc).RootAccountId;
            var id = profile.ProfileInfo?.ProfileId?.ToString();
            if (!string.IsNullOrEmpty(owner) && owner != id && id != null)
            {
                Register(id);
            }
        }
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (MongoId.IsValidMongoId(id))
            {
                Register(id);
            }
        }
        foreach (var id in Characters.Keys)
        {
            Migrate(id);
            if (!saves.ProfileExists(new MongoId(id)) && File.Exists(Path.Combine(DirectoryPath, id + ".json")))
            {
                await saves.LoadProfileAsync(new MongoId(id));
            }
        }
    }

    private static void Migrate(string id)
    {
        var source = Path.GetFullPath(Path.Combine("user/profiles", id + ".json"));
        var destination = Path.Combine(DirectoryPath, id + ".json");
        if (!File.Exists(source))
        {
            return;
        }

        static string Hash(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        var hash = Hash(source);
        if (File.Exists(destination) && Hash(destination) != hash)
        {
            throw new InvalidDataException("Conflicting campaign profile copies require recovery: " + id);
        }

        var backup = Path.Combine(Path.GetDirectoryName(DirectoryPath)!, "migration-backups", id + "-" + hash + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup))
        {
            File.Copy(source, backup);
        }

        if (Hash(backup) != hash)
        {
            throw new IOException("Campaign profile backup verification failed: " + id);
        }

        if (!File.Exists(destination))
        {
            File.Copy(source, destination);
        }

        if (Hash(destination) != hash)
        {
            throw new IOException("Campaign profile migration verification failed: " + id);
        }

        File.Delete(source);
    }

    // Called only from patched SPT profile/backup methods, not from general filesystem operations.
    public static string Combine(string directory, string filename)
    {
        var scratch = Editor.EditorSessions.ScratchPath(directory, filename);
        if (scratch != null)
            return scratch;
        if (
            Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath("user/profiles"), StringComparison.OrdinalIgnoreCase)
        )
        {
            var id = Path.GetFileNameWithoutExtension(filename).Split('-')[0];
            if (Contains(id))
            {
                return Path.Combine(DirectoryPath, filename);
            }
        }
        return Path.Combine(directory, filename);
    }

    public static List<string> BackupFiles(FileUtil files, string directory, bool recursive, string searchPattern)
    {
        var result = files.GetFiles(directory, recursive, searchPattern);
        if (Directory.Exists(DirectoryPath))
        {
            result.AddRange(
                Directory.EnumerateFiles(DirectoryPath, "*.json").Where(p => MongoId.IsValidMongoId(Path.GetFileNameWithoutExtension(p)))
            );
        }

        return result;
    }
}
