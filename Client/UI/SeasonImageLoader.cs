using System.Globalization;
using Cysharp.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;

namespace WTT.Campaigns.Client.UI;

internal static class SeasonImageLoader
{
    private static readonly ImageRequestCache Cache = new();

    internal static string PathFor(string collection, string id)
    {
        var snapshot = Plugin.Current;
        return "/wtt-campaigns/"
            + collection
            + "/"
            + Uri.EscapeDataString(id)
            + ".png?season="
            + Uri.EscapeDataString(snapshot?.SeasonId ?? "")
            + "&revision="
            + (snapshot?.PackRevision ?? 0).ToString(CultureInfo.InvariantCulture);
    }

    internal static async UniTask<Texture2D> LoadAsync(string path, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var key = RequestHandler.Host + "\n" + path;
        // A closing screen must not cancel a download shared with another screen.
        var download = Cache.GetAsync(key, () => RequestHandler.GetDataAsync(path));
        var bytes = cancellation.CanBeCanceled ? await download.AsUniTask().AttachExternalCancellation(cancellation) : await download;
        // Only the waiting screen is cancelled; decoding and Unity objects always stay on the main thread.
        await UniTask.SwitchToMainThread(cancellation);
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
