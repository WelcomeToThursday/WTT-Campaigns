using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using ZLinq;

namespace WTT.Campaigns.Client.Spatial;

internal sealed class MapLayerUi : MonoBehaviour
{
    internal static MapLayerUi Instance = null!;
    private GameObject? _canvas;
    private MapLayersScreen? _screen;
    private bool _busy,
        _destroyed;
    private int _blockedThrough = -1;
    private long _revision;
    private string _character = "";
    internal bool IsOpen => _screen != null && _screen.Root && _screen.Root.activeSelf;
    internal bool InputBlocked => IsOpen || Time.frameCount <= _blockedThrough;
    internal static bool Available =>
        !Plugin.InRaid
        && !Authoring.EditorMode.Active
        && !Authoring.CampaignTestMode.Restricted
        && !Plugin.Busy
        && Plugin.Current?.ActiveMode == "normal"
        && Plugin.App?.Session?.Profile?.Id == Plugin.Current.EffectiveProfileId;

    private void Awake() => Instance = this;

    private void Update()
    {
        if (!IsOpen)
            return;
        if (!Available || _character != Plugin.App?.Session?.Profile?.Id)
        {
            Close(force: true);
            return;
        }
        _screen!.Fit();
        if (Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    internal void Open()
    {
        if (!Available || _busy)
            return;
        SeasonUi.Instance.CloseForNavigation();
        Hub.SeasonHubUi.Instance.Close();
        EnsureScreen();
        _character = Plugin.App!.Session.Profile.Id;
        _screen!.SetState(Array.Empty<MapLayerEntry>());
        _screen.Open();
        Request(false);
    }

    private void EnsureScreen()
    {
        if (_screen != null)
            return;
        _canvas = new GameObject("MapLayersCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_canvas);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29010;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var font =
            Resources
                .FindObjectsOfTypeAll<Font>()
                .AsValueEnumerable()
                .FirstOrDefault(f => f.name.Equals("Jovanny Lemonad - Bender", StringComparison.OrdinalIgnoreCase))
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        _screen = new MapLayersScreen(_canvas.transform, font)
        {
            CloseRequested = () => Close(),
            RefreshRequested = () => Request(false),
            ToggleRequested = (key, enabled) => Request(true, key, enabled),
            SoundRequested = SeasonUi.Instance.PlayInterfaceSound,
        };
    }

    private async void Request(bool change, string key = "", bool enabled = false)
    {
        if (_busy || !Available)
            return;
        _busy = true;
        var character = _character;
        _screen!.Status(change ? "Saving selection…" : "Loading map layers…", true);
        try
        {
            var json = await RequestHandler.PostJsonAsync(
                "/wtt-campaigns/map-layers/" + (change ? "select" : "options"),
                JsonConvert.SerializeObject(
                    new MapLayerOptionsRequest
                    {
                        CharacterId = character,
                        Key = key,
                        Enabled = enabled,
                        Revision = _revision,
                    }
                )
            );
            if (_destroyed || !IsOpen || character != _character || character != Plugin.App?.Session?.Profile?.Id)
                return;
            var response =
                JsonConvert.DeserializeObject<MapLayerOptionsResponse>(json)
                ?? throw new InvalidOperationException("No map layer response was received.");
            if (response.Error != null)
                throw new InvalidOperationException(response.Error);
            if (response.CharacterId != character)
                throw new InvalidOperationException("Map layer selections belong to another character.");
            _revision = response.Revision;
            _screen.SetState(
                response
                    .Layers.AsValueEnumerable()
                    .Select(l => new MapLayerEntry
                    {
                        Key = l.Key,
                        Name = l.Name,
                        Campaign = l.Campaign,
                        Location = Plugin.Localized(l.Location, l.Location),
                        Enabled = l.Enabled,
                    })
                    .ToArray(),
                change ? "Saved. This selection applies on your next raid." : "Choose which layers to enable for each map."
            );
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (!_destroyed && IsOpen && character == _character)
                _screen!.Status(exception.Message, false, true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Close(bool force = false)
    {
        if (_busy && !force)
            return;
        _screen?.Close();
        _blockedThrough = Time.frameCount + 1;
    }

    private void OnDestroy()
    {
        _destroyed = true;
        _screen?.Dispose();
        if (_canvas)
            Destroy(_canvas);
        if (Instance == this)
            Instance = null!;
    }
}
