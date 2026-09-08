using System.Globalization;
using SPT.Common.Http;
using UnityEngine;

namespace SeasonalPerks.Client.UI;

internal static class SeasonImageLoader
{
    private static readonly ImageRequestCache Cache = new();

    internal static string PathFor(string collection, string id)
    {
        var snapshot = Plugin.Current;
        return "/wtt-seasonal/"
            + collection
            + "/"
            + Uri.EscapeDataString(id)
            + ".png?season="
            + Uri.EscapeDataString(snapshot?.SeasonId ?? "")
            + "&revision="
            + (snapshot?.PackRevision ?? 0).ToString(CultureInfo.InvariantCulture);
    }

    internal static async Task<Texture2D> LoadAsync(string path, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var key = RequestHandler.Host + "\n" + path;
        // A closing screen must not cancel a download shared with another screen.
        var bytes = await Cache.GetAsync(key, () => RequestHandler.GetDataAsync(path));
        cancellation.ThrowIfCancellationRequested();
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes) || texture.width > 4096 || texture.height > 4096)
            {
                throw new InvalidDataException("Invalid seasonal image: " + path);
            }
            return texture;
        }
        catch
        {
            Cache.Reject(key, bytes);
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }
}
