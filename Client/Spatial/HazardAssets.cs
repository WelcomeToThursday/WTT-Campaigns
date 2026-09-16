using System.IO;
using System.Threading;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WTT.Campaigns.Client.Spatial;

// Leases native resources until the owning hazard has been destroyed.
internal sealed class HazardAssets : IDisposable
{
    private const string RifleBundle = "assets/content/audio/banks/mosin.bundle";
    private static readonly string DirectoryPath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "HazardLibrary");
    private static Task<AssetBundle>? _loading;
    private static AssetBundle? _bundle;
    private static int _references;
    private bool _wireLease;
    private DependencyGraph<IEasyBundle>.Token? _rifleLease;
    internal Mesh? WireMesh;
    internal Material? WireMaterial;
    internal SoundBank? WireSound;
    internal SoundBank? RifleSound;

    internal static async Task<HazardAssets> Load(string kind, bool playSound, bool suppressed, CancellationToken token)
    {
        var result = new HazardAssets();
        try
        {
            if (kind == "BarbedWire")
            {
                _references++;
                result._wireLease = true;
                var bundle = await (_loading ??= LoadWireBundle());
                token.ThrowIfCancellationRequested();
                result.WireMesh = await Asset<Mesh>(bundle, "hazards/wire.mesh", token);
                result.WireMaterial = await Asset<Material>(bundle, "hazards/wire.mat", token);
                result.WireSound = await Asset<SoundBank>(bundle, "hazards/wire.sound", token);
            }
            else if (kind == "Sniper" && playSound)
            {
                var assets = Singleton<ObjectsFactory>.Instance.EasyAssets;
                if (!assets.System.Nodes.TryGetValue(RifleBundle, out var node) || node.Data is not EasyBundle bundle)
                    throw new InvalidOperationException("Native sniper sound bundle is unavailable.");
                result._rifleLease = assets.Retain(new[] { RifleBundle }, ct: token);
                await result._rifleLease.LoadingJob;
                token.ThrowIfCancellationRequested();
                result.RifleSound = await Asset<SoundBank>(
                    bundle._bundle,
                    "assets/content/audio/banks/mosin/mosin_main" + (suppressed ? "_silenced" : "") + ".asset",
                    token
                );
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static async Task<T> Asset<T>(AssetBundle bundle, string name, CancellationToken token)
        where T : UnityEngine.Object
    {
        if (!bundle)
            throw new InvalidOperationException("Native hazard bundle failed to load.");
        var request = bundle.LoadAssetAsync<T>(name);
        // Unity asset requests are not cancellable; retain resources until they finish.
        while (!request.isDone)
            await UniTask.NextFrame();
        token.ThrowIfCancellationRequested();
        return request.asset as T ?? throw new InvalidOperationException("Native hazard asset is missing: " + name);
    }

    private static async Task<AssetBundle> LoadWireBundle()
    {
        var catalog = JObject.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "catalog.json")));
        using (var file = File.OpenRead(Path.Combine(DirectoryPath, "native-hazards.bundle")))
        using (var sha = System.Security.Cryptography.SHA256.Create())
            if ((int?)catalog["Schema"] != 1 || BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != (string?)catalog["Sha256"])
                throw new InvalidOperationException("Native hazard library does not match its validated catalog.");
        var request = AssetBundle.LoadFromFileAsync(Path.Combine(DirectoryPath, "native-hazards.bundle"));
        while (!request.isDone)
            await UniTask.NextFrame();
        return _bundle = request.assetBundle
            ? request.assetBundle
            : throw new InvalidOperationException("Native hazard library failed to load.");
    }

    public void Dispose()
    {
        _rifleLease?.Release();
        _rifleLease = null;
        if (!_wireLease)
            return;
        _wireLease = false;
        if (--_references == 0)
            _ = UnloadAfterDestruction();
    }

    private static async Task UnloadAfterDestruction()
    {
        await UniTask.NextFrame();
        if (_references != 0)
            return;
        if (_bundle)
            _bundle!.Unload(true);
        _bundle = null;
        _loading = null;
    }
}
