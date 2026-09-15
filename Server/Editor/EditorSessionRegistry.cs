using System.Collections.Concurrent;
using SPTarkov.Server.Core.Models.Common;

namespace WTT.Campaigns.Server.Editor;

public class EditorSessionRegistry
{
    public enum ResolutionStatus
    {
        Accepted,
        MissingIdentity,
        RetiredScratch,
        MismatchedIdentity,
        NotReady,
        MismatchedToken,
        Expired,
        MissingMap,
    }

    public readonly record struct Resolution(Session? Session, ResolutionStatus Status)
    {
        public bool Accepted => Session != null && Status == ResolutionStatus.Accepted;

        public Session RequireMap(string operation)
        {
            if (Accepted)
                return Session!;

            throw new InvalidOperationException(
                Status switch
                {
                    ResolutionStatus.MismatchedIdentity or ResolutionStatus.MismatchedToken => "Editor session does not match.",
                    ResolutionStatus.RetiredScratch or ResolutionStatus.NotReady or ResolutionStatus.Expired =>
                        "Editor session expired. Return to editor home and reconnect.",
                    ResolutionStatus.MissingMap => "Connect an active editor map before " + operation + ".",
                    _ => "Connect an active editor map before " + operation + ".",
                }
            );
        }
    }

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

        public bool AcceptsMapRequest(string owner, WTT.Campaigns.Shared.Authoring.AuthoringRequest request, DateTimeOffset now) =>
            request.Version is 2 or 3 or 4 or 5
            && Accepts(owner, request.EditorSessionId, now)
            && Location.Length > 0
            && request.Location == Location;
    }

    protected static readonly ConcurrentDictionary<string, Session> Profiles = new();

    // Tombstones keep late native saves isolated after a session has ended.
    protected static readonly ConcurrentDictionary<string, byte> ScratchIds = new();
    public static string ScratchDirectory => Path.GetFullPath("user/wtt-campaigns/editor-sessions");

    public static bool IsScratch(string id) => ScratchIds.ContainsKey(id);

    public static Session? Find(string profile) => Profiles.GetValueOrDefault(profile);

    /// <summary>
    /// Resolves only the authenticated transport identity. The request token is
    /// checked after identity resolution and is never used to discover a session.
    /// </summary>
    public static Resolution Resolve(string transportIdentity, string token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(transportIdentity))
            return new(null, ResolutionStatus.MissingIdentity);

        // A retired scratch profile must never fall through to an owner or return
        // profile that happens to share the same text.
        if (IsScratch(transportIdentity) && Find(transportIdentity) == null)
            return new(null, ResolutionStatus.RetiredScratch);

        var matches = Profiles
            .Values.Where(s =>
                Find(s.Profile) == s
                && (s.Profile == transportIdentity || s.Owner == transportIdentity || s.ReturnProfile == transportIdentity)
            )
            .ToArray();
        if (matches.Length == 0)
            return new(null, ResolutionStatus.MissingIdentity);
        if (matches.Length != 1)
            return new(null, ResolutionStatus.MismatchedIdentity);

        var session = matches[0];
        if (!session.Ready)
            return new(session, ResolutionStatus.NotReady);
        if (session.Id != token)
            return new(session, ResolutionStatus.MismatchedToken);
        if (now - session.Contact > TimeSpan.FromMinutes(1))
            return new(session, ResolutionStatus.Expired);
        if (session.Location.Length == 0)
            return new(session, ResolutionStatus.MissingMap);
        return new(session, ResolutionStatus.Accepted);
    }

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
