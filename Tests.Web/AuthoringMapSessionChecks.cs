using System.Reflection;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Server.Routing;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Web.Tests;

internal static class AuthoringMapSessionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var validate = typeof(AuthoringSocketHandler).GetMethod("AcceptsMapRequest", BindingFlags.NonPublic | BindingFlags.Static)!;
        var now = DateTimeOffset.UtcNow;
        var session = new EditorSessionRegistry.Session
        {
            Owner = "owner",
            Ready = true,
            Location = "woods",
            Contact = now,
        };
        var request = new AuthoringRequest { EditorSessionId = session.Id, Location = session.Location };
        bool Accepts(string owner = "owner")
        {
            return (bool)validate.Invoke(null, [session, owner, request, now])!;
        }
        foreach (var version in new[] { 2, 3, 4, 5, 6 })
        {
            request.Version = version;
            check(Accepts(), $"Editor socket accepts matching map session protocol {version}");
        }
        foreach (var version in new[] { -1, 0, 1, 7, int.MaxValue })
        {
            request.Version = version;
            check(!Accepts(), $"Editor socket rejects unsupported map protocol {version}");
        }
        request.Version = 6;
        check(!Accepts("other-owner"), "Version 6 retains owner isolation");
        request.EditorSessionId = Guid.NewGuid().ToString("N");
        check(!Accepts(), "Version 6 rejects stale session tokens");
        request.EditorSessionId = session.Id;
        request.Location = "shoreline";
        check(!Accepts(), "Version 6 rejects the wrong map");
        request.Location = session.Location;
        session.Ready = false;
        check(!Accepts(), "Version 6 rejects retired sessions");
        session.Ready = true;
        session.Contact = now - TimeSpan.FromMinutes(2);
        check(!Accepts(), "Version 6 rejects expired sessions");
    }
}
