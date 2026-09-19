using System.Security.Cryptography;
using System.Text;

namespace WTT.Campaigns.Shared.Spatial;

public static class SceneAssetRules
{
    public static bool IsContainer(MapObjectEdit placement) =>
        placement.Operation == "Copy" && placement.Target?.Kind is "AssetContainer" or "Container";

    public static bool SafePath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 512
        && !value.StartsWith("/")
        && !value.Contains('\\')
        && !value.Contains(':')
        && value.Split('/').AsValueEnumerable().All(p => p.Length > 0 && p is not "." and not "..");

    public static string CacheFingerprint(IEnumerable<string> dependencyEvidence) =>
        Identity("scene-index-v1", dependencyEvidence.AsValueEnumerable().OrderBy(e => e, StringComparer.Ordinal).JoinToString("\n"));

    public static string Identity(string bundle, string asset)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(bundle + "\n" + asset))).Replace("-", "");
    }

    public static bool Valid(MapTarget target) =>
        target.IsAsset
        && SafePath(target.Bundle)
        && SafePath(target.Asset)
        && target.Fingerprint == Identity(target.Bundle, target.Asset)
        && (target.Kind != "AssetContainer" || WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(target.Template));
}
