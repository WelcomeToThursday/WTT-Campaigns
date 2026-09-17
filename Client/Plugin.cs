using BepInEx;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Client.Authoring.Preview;
using WTT.Campaigns.Client.Hub;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Effects;
using WTT.Campaigns.Shared.Perks;
using WTT.Campaigns.Shared.Profiles;

namespace WTT.Campaigns.Client;

[BepInPlugin("com.wtt.campaigns", "WTT-Campaigns", "0.10.0")]
[BepInDependency("com.SPT.custom", "4.1.0")]
[BepInDependency("com.arys.unitytoolkit", "2.0.2")]
[BepInDependency("com.wtt.commonlib", "3.0.6")]
[BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
[BepInDependency("me.sol.sain", "4.5.1")]
[BepInDependency("com.morebotsapi.tacticaltoaster", "2.1.1")]
[BepInDependency("com.blackdiv.tacticaltoaster", "1.3.1")]
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
        get
        {
            return !Authoring.EditorMode.Active
                && Player != null
                && Current?.ActiveMode == "seasonal"
                && Player.Profile.Id == App?.Session?.Profile?.Id;
        }
    }

    internal static string Folder
    {
        get { return Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!; }
    }

    private void Awake()
    {
        Instance = this;
        gameObject.AddComponent<Authoring.EditorMode>();
        gameObject.AddComponent<Authoring.CampaignTestMode>();
        Patches.PatchRegistration.EnableAll();
        gameObject.AddComponent<SeasonUi>();
        WTT.Campaigns.UI.Media.StoryUiArtwork.SharedStatusIcon = name =>
            SeasonUi.Instance.UiBundle.LoadAsset<UnityEngine.Sprite>("assets/mods/wtt-campaigns.assets/storystatusicons/" + name + ".png");
        gameObject.AddComponent<SeasonHubUi>();
        gameObject.AddComponent<Spatial.ZoneRuntime>();
        gameObject.AddComponent<Missions.MissionUi>();
        gameObject.AddComponent<Missions.MissionRaidRuntime>();
        gameObject.AddComponent<Spatial.MapLayerRuntime>();
        gameObject.AddComponent<Spatial.MapLayerUi>();
        gameObject.AddComponent<Authoring.RaidEditor>();
        gameObject.AddComponent<ItemPreviewClient>();
        gameObject.AddComponent<Story.StoryRaidRuntime>();
        gameObject.AddComponent<Story.StoryVisitRuntime>();
        gameObject.AddComponent<Story.StoryCinematicRuntime>();
    }

    internal static string Localized(string key, string fallback)
    {
        var text = key.Localized();
        return string.IsNullOrEmpty(text) || text == key ? fallback : text;
    }

    internal static void Accept(ClientSnapshot snapshot)
    {
        if (snapshot.Error != null)
        {
            throw new InvalidOperationException(snapshot.Error);
        }

        if (snapshot.ProtocolVersion != 2)
        {
            throw new InvalidOperationException("Update both WTT-Campaigns client and server to the same version.");
        }

        snapshot.SeasonName = Localized(snapshot.SeasonId + " name", snapshot.SeasonName);
        Current = snapshot;
        Effects = new RuntimeEffects(
            snapshot.Catalogue,
            snapshot.ActiveMode == "seasonal" ? snapshot.State.SeasonalPerks : Array.Empty<string>(),
            snapshot.State.SeasonalPerkEffectParameters
        );
    }

    internal static async Task<ClientSnapshot> Request(string operation, Mutation? mutation = null)
    {
        mutation ??= new Mutation();
        mutation.ProtocolVersion = 2;
        if (mutation.SeasonId.Length == 0 && operation != "snapshot")
        {
            mutation.SeasonId = Current?.SeasonId ?? "";
        }

        var json = await RequestHandler.PostJsonAsync("/wtt-campaigns/" + operation, JsonConvert.SerializeObject(mutation));
        var snapshot =
            JsonConvert.DeserializeObject<ClientSnapshot>(json, EftJsonConverters.Converters)
            ?? throw new InvalidDataException("WTT-Campaigns server returned an empty response.");
        if (snapshot.Error != null)
        {
            throw new InvalidOperationException(snapshot.Error);
        }

        return snapshot;
    }

    internal static async Task FlushPendingOperations()
    {
        var app = App ?? throw new InvalidOperationException("The game menu is not ready.");
        LogInfo("WTT-Campaigns switch/save: flushing pending operations.");
        var result = await app.Session.FlushOperationQueue();
        if (!result.Succeed)
        {
            throw new InvalidOperationException("Pending profile operations could not be saved: " + result.Error);
        }
        LogInfo("WTT-Campaigns switch/save: pending operations saved.");
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
            LogInfo("WTT-Campaigns switch/save: reconnecting to " + snapshot.ActiveMode + ".");
            // EFT deliberately starts old-session shutdown without awaiting the websocket close.
            // The CreateBackend patch applies the target identity at the new connection boundary.
            await ProfileReconnect.Run(
                snapshot,
                SeasonUi.Instance.SetReconnectOverlayVisible,
                () => app.RecreateBackend(mode, force: true)
            );
            if (!CharacterSession.IsLoaded(snapshot, snapshot.ActiveMode, app.Session?.Profile?.Id))
            {
                throw new InvalidOperationException("The requested character did not finish loading. Select it again to retry.");
            }
            Accept(snapshot);
            LogInfo("WTT-Campaigns switch/save: " + snapshot.ActiveMode + " character loaded and verified.");
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
