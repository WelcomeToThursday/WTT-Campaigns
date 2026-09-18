using System.Security.Cryptography;

namespace WTT.Campaigns.Server.Web;

/// <summary>Keep every Creator stylesheet link tied to the installed file contents.</summary>
public static class CreatorStyles
{
    private static readonly Lazy<string> VersionedUrl = new(() =>
    {
        var path = Path.Combine(Metadata.DirectoryPath, "wwwroot", "creator.css");
        using var stream = File.OpenRead(path);
        var version = Convert.ToHexString(SHA256.HashData(stream));
        return "/wtt-campaigns-creator-assets/creator.css?v=" + version;
    });

    public static string Url => VersionedUrl.Value;
}
