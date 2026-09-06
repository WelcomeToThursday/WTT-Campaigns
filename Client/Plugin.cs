using BepInEx;
using EFT;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;
using SPT.Common.Http;

namespace SeasonalPerks.Client;

[BepInPlugin("com.cj.seasonalperks", "Seasonal Perks", "0.1.24")]
[BepInDependency("com.SPT.custom", "4.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance = null!;
    internal static ClientSnapshot? Current;
    internal static string? SessionId;
    internal static string? PendingSessionId;
    internal static RuntimeEffects Effects = new(new Catalogue(), Array.Empty<string>());
    internal static bool Busy;
    private static TarkovApplication? _application;
    internal static TarkovApplication? App
    {
        get { return _application ? _application : _application = UnityEngine.Object.FindObjectOfType<TarkovApplication>(); }
    }

    internal static Player? Player
    {
        get { return Comfort.Common.Singleton<GameWorld>.Instantiated ? Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer : null; }
    }

    internal static bool InRaid
    {
        get { return Player != null && !(Comfort.Common.Singleton<GameWorld>.Instance is HideoutGameWorld); }
    }

    internal static bool SeasonalPlayer
    {
        get { return Player != null && Current?.ActiveMode == "seasonal" && Player.Profile.Id == App?.Session?.Profile?.Id; }
    }

    internal static string Folder
    {
        get { return Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!; }
    }

    private void Awake()
    {
        Instance = this;
        Patches.PatchRegistration.EnableAll();
        gameObject.AddComponent<SeasonUi>();
        gameObject.AddComponent<SeasonHubUi>();
    }

    internal static void Accept(ClientSnapshot snapshot)
    {
        if (snapshot.Error != null)
        {
            throw new InvalidOperationException(snapshot.Error);
        }

        Current = snapshot;
        Effects = new RuntimeEffects(
            snapshot.Catalogue,
            snapshot.ActiveMode == "seasonal" ? snapshot.State.SeasonalPerks : Array.Empty<string>(),
            snapshot.State.SeasonalPerkEffectParameters
        );
    }

    internal static async Task<ClientSnapshot> Request(string operation, Mutation? mutation = null)
    {
        var json = await RequestHandler.PostJsonAsync(
            "/seasonal-perks/" + operation,
            JsonConvert.SerializeObject(mutation ?? new Mutation())
        );
        var snapshot =
            JsonConvert.DeserializeObject<ClientSnapshot>(json, EftJsonConverters.Converters)
            ?? throw new InvalidDataException("Seasonal server returned an empty response.");
        if (snapshot.Error != null)
        {
            throw new InvalidOperationException(snapshot.Error);
        }

        return snapshot;
    }

    internal static async Task FlushPendingOperations()
    {
        var app = App ?? throw new InvalidOperationException("The game menu is not ready.");
        LogInfo("Seasonal switch/save: flushing pending operations.");
        var result = await app.Session.FlushOperationQueue();
        if (!result.Succeed)
        {
            throw new InvalidOperationException("Pending profile operations could not be saved: " + result.Error);
        }
        LogInfo("Seasonal switch/save: pending operations saved.");
    }

    // Callers flush once before the server mutation; never flush again after switching its identity.
    internal static async Task Reload(ClientSnapshot snapshot)
    {
        var app = App ?? throw new InvalidOperationException("The game menu is not ready.");
        if (InRaid)
        {
            throw new InvalidOperationException("Finish the raid first.");
        }

        var mode = app.Session.SessionMode;
        PendingSessionId = snapshot.EffectiveProfileId;
        try
        {
            LogInfo("Seasonal switch/save: reconnecting to " + snapshot.ActiveMode + ".");
            // EFT deliberately starts old-session shutdown without awaiting the websocket close.
            // The CreateBackend patch applies the target identity at the new connection boundary.
            await app.RecreateBackend(mode, force: true);
            if (!CharacterSession.IsLoaded(snapshot, snapshot.ActiveMode, app.Session?.Profile?.Id))
            {
                throw new InvalidOperationException("The requested character did not finish loading. Select it again to retry.");
            }
            Accept(snapshot);
            LogInfo("Seasonal switch/save: " + snapshot.ActiveMode + " character loaded and verified.");
        }
        finally
        {
            PendingSessionId = null;
        }
    }

    internal static void Error(Exception e)
    {
        Instance.Logger.LogError(e);
    }

    internal static void LogInfo(string message)
    {
        Instance.Logger.LogInfo(message);
    }
}
