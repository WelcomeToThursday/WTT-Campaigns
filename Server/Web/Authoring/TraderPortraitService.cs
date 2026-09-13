using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Image;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Web.Authoring;

public sealed record TraderPortraitFile(string Path, string ContentType, string Version);

[Injectable(InjectionType.Singleton)]
public sealed class TraderPortraitService(TradersTable traders, ImageRouterService images, FileUtil files)
{
    public TraderPortraitFile? Find(string traderId)
    {
        if (!SeasonValidator.IsId(traderId) || !traders.TryGetValue(new MongoId(traderId), out var trader))
            return null;
        var avatar = trader.Base?.Avatar;
        if (string.IsNullOrWhiteSpace(avatar))
            return null;
        // Resolve the configured path exactly as SPT's image listener does. Mod
        // portraits need not share a filename with their trader's identity.
        if (Uri.TryCreate(avatar, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            avatar = uri.AbsolutePath;
        avatar = avatar.Split('?', '#')[0];
        var key = Uri.UnescapeDataString(files.StripExtension(avatar, keepPath: true)).ToLowerInvariant();
        if (!images.ExistsByKey(key))
            return null;
        var path = images.GetByKey(key);
        var type = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".avif" => "image/avif",
            _ => null,
        };
        if (type == null || !File.Exists(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            return new(info.FullName, type, $"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}");
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
