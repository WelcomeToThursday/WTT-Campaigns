using System.Collections.Concurrent;
using SPTarkov.Server.Core.Models.Common;

namespace WTT.Campaigns.Server.Editor;

public class EditorSessionRegistry
{
    public sealed class Session
    {
        public string Id = Guid.NewGuid().ToString("N"),
            Owner = "",
            Profile = "",
            ReturnProfile = "",
            Draft = "",
            Layout = "",
            Location = "";
        public DateTimeOffset Contact = DateTimeOffset.UtcNow;
        public bool Ready;

        public void Select(string draft, string layout)
        {
            if (Location.Length > 0)
                throw new InvalidOperationException("Unload the map before changing drafts.");
            Draft = draft;
            Layout = layout;
        }

        public void OpenMap(string location)
        {
            if (!Ready || Draft.Length == 0 || string.IsNullOrWhiteSpace(location) || location.Length > 120)
                throw new InvalidOperationException("Connect an editor session and select a draft and map first.");
            if (Location.Length > 0 && Location != location)
                throw new InvalidOperationException("Unload the current map first.");
            Location = location;
        }

        public void UnloadMap() => Location = "";

        public void CheckCanEnd()
        {
            if (Location.Length > 0)
                throw new InvalidOperationException("Unload the editor map before returning to the game.");
        }

        public bool Accepts(string owner, string token, DateTimeOffset now) =>
            Ready && Owner == owner && Id == token && now - Contact <= TimeSpan.FromMinutes(1);
    }

    protected static readonly ConcurrentDictionary<string, Session> Profiles = new();

    // Tombstones keep late native saves isolated after a session has ended.
    protected static readonly ConcurrentDictionary<string, byte> ScratchIds = new();
    public static string ScratchDirectory => Path.GetFullPath("user/wtt-campaigns/editor-sessions");

    public static bool IsScratch(string id) => ScratchIds.ContainsKey(id);

    public static Session? Find(string profile) => Profiles.GetValueOrDefault(profile);

    public static Session? ForOwner(string owner) => Profiles.Values.FirstOrDefault(s => s.Owner == owner && s.Ready);

    public static string Owner(string id) => Find(id)?.Owner ?? id;

    public static bool Restricted(string id) => IsScratch(id) || Profiles.Values.Any(s => s.Owner == id || s.ReturnProfile == id);

    public static string? ScratchPath(string directory, string filename)
    {
        var id = Path.GetFileNameWithoutExtension(filename).Split('-')[0];
        return
            IsScratch(id)
            && Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath("user/profiles"), StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(ScratchDirectory, filename)
            : null;
    }

    public static void ClearAbandonedFiles()
    {
        Directory.CreateDirectory(ScratchDirectory);
        foreach (var file in Directory.EnumerateFiles(ScratchDirectory, "*.json", SearchOption.TopDirectoryOnly))
            if (MongoId.IsValidMongoId(Path.GetFileNameWithoutExtension(file)))
                File.Delete(file);
    }
}
