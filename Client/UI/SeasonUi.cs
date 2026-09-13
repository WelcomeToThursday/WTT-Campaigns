using Comfort.Common;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Hub;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Profiles;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using ZLinq;

namespace WTT.Campaigns.Client.UI;

public sealed partial class SeasonUi : MonoBehaviour
{
    internal static SeasonUi Instance = null!;
    internal AssetBundle UiBundle
    {
        get
        {
            return _bundle ??=
                AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_ui.bundle"))
                ?? throw new InvalidDataException("Missing campaign UI bundle.");
        }
    }
    private readonly Dictionary<string, Task<Sprite>> _images = new();
    private AssetBundle? _bundle;
    private GameObject? _canvas;
    private GameObject? _creationLoader;
    private CampaignScreen? _screen;
    private bool _opening;
    private bool _destroyed;
    private int _inputBlockedThrough = -1;
    private bool _startupShown;
    private bool _startup;

    internal void ShowStartupSelection()
    {
        if (Authoring.EditorMode.Active)
            return;
        if (_startupShown)
        {
            return;
        }

        _startupShown = true;
        _startup = true;
        if (!_destroyed)
        {
            Open();
        }
    }

    internal bool IsOpen
    {
        get { return _screen != null && _screen.Root && _screen.Root.activeSelf; }
    }

    internal bool InputBlocked
    {
        get { return IsOpen || (SeasonHubUi.Instance && SeasonHubUi.Instance.InputBlocked) || Time.frameCount <= _inputBlockedThrough; }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (Authoring.EditorMode.Active)
        {
            if (IsOpen)
                Close();
            return;
        }
        if (IsOpen)
        {
            _screen!.Fit();
            if (Plugin.InRaid)
            {
                Close();
            }
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F8))
            {
                _screen.RequestCloseFromInput();
            }
            else if (_screen.SeasonIntroductionOpen)
            {
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    _screen.ChangeSeasonIntroductionPage(-1);
                }
                else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow))
                {
                    _screen.ChangeSeasonIntroductionPage(1);
                }
            }
        }
        else if (Input.GetKeyDown(KeyCode.F8) && !Plugin.InRaid && !Plugin.Busy)
        {
            PlayInterfaceSound(InterfaceSound.ButtonClick);
            Open();
        }
    }

    internal async void Open(ScreenPage page = ScreenPage.Characters)
    {
        if (Authoring.EditorMode.Active || Plugin.InRaid || Plugin.Busy || _opening)
        {
            return;
        }
        if (SeasonHubUi.Instance)
        {
            SeasonHubUi.Instance.Close();
        }

        if (IsOpen)
        {
            _screen!.ShowPage(page);
            return;
        }
        _opening = true;
        try
        {
            EnsureScreen();
            _screen!.StartupSelection = _startup;
            _screen!.SetBusy(false);
            _screen.Open(page);
            _screen.SetBusy(true, "Loading campaign characters...");
            var snapshot = await Plugin.Request("snapshot");
            if (_destroyed)
            {
                return;
            }
            Plugin.Accept(snapshot);
            _screen.SetBusy(false);
            _screen.SetState(Presentation(snapshot), page);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _screen?.SetBusy(false);
            _screen?.SetMessage(exception.Message, true);
        }
        finally
        {
            _opening = false;
        }
    }

    private void EnsureScreen()
    {
        if (_screen != null)
        {
            return;
        }
        _canvas = new GameObject("CampaignsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_canvas);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29000;
        canvas.pixelPerfect = true;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _screen = CreateView(_canvas.transform);
        _screen.CloseRequested = Close;
        _screen.SaveRequested = Save;
        _screen.SwitchRequested = Switch;
        _screen.SeasonChosen = ChooseSeason;
        _screen.RecreationRequested = (id, season) => ChooseSeason(season, id);
        _screen.CharacterManagementRequested = ManageCharacter;
    }

    internal CampaignScreen CreateView(Transform parent, bool embedded = false)
    {
        _bundle ??=
            AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_ui.bundle"))
            ?? throw new InvalidDataException("Missing campaign UI bundle.");
        var font =
            _bundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf")
            ?? Resources
                .FindObjectsOfTypeAll<Font>()
                .AsValueEnumerable()
                .FirstOrDefault(value => value.name.Equals("Jovanny Lemonad - Bender", StringComparison.OrdinalIgnoreCase))
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        var view = new CampaignScreen(
            parent,
            layoutName =>
                _bundle.LoadAsset<GameObject>("assets/mods/wtt-campaigns.assets/ui/" + layoutName + ".prefab")
                ?? throw new InvalidDataException("Missing campaign UI layout: " + layoutName),
            font,
            embedded
        );
        view.IconRequested = LoadIcon;
        view.SoundRequested = PlayInterfaceSound;
        view.ProfileHoverSound = PlayProfileHover;
        view.GlowMaterial = _bundle.LoadAsset<Material>("assets/mods/wtt-campaigns.assets/ui/selection-additive.mat");
        view.ArtworkRequested = LoadArtwork;
        view.CharacterRequested = LoadCharacter;
        view.IdentityRequested = (host, draft, complete, back) => new CreationIdentity(host, draft, font, complete, back);
        return view;
    }

    internal static ScreenState Presentation(ClientSnapshot snapshot)
    {
        return new ScreenState
        {
            ActiveMode = snapshot.ActiveMode,
            SeasonId = snapshot.SeasonId,
            SelectedCharacterId = snapshot.SelectedCharacterId,
            Seasons = snapshot
                .Seasons.AsValueEnumerable()
                .Select(season => new SeasonEntry
                {
                    Id = season.Id,
                    Name = season.Name,
                    Description = season.Description,
                })
                .ToArray(),
            StartingPoints = snapshot.Rules.StartingPoints,
            EnforceBudget = snapshot.Rules.EnforceBudget,
            AllowEdits = snapshot.Rules.AllowEdits,
            Selected = snapshot
                .State.SeasonalPerks.AsValueEnumerable()
                .Where(id => snapshot.Catalogue.Personal.AsValueEnumerable().Any(perk => perk.Id == id))
                .ToArray(),
            Characters = snapshot
                .Characters.AsValueEnumerable()
                .Select(character => new CharacterEntry
                {
                    Mode = character.Mode,
                    Id = character.Id,
                    SeasonId = character.SeasonId,
                    SeasonName = character.SeasonName,
                    Available = character.Available,
                    Name = character.Name,
                    Level = character.Level,
                    StoryChapters = character.StoryChapters,
                    Exists = character.Exists,
                    Wiped = character.Wiped,
                    Side = character.Side,
                })
                .ToArray(),
            Perks = snapshot
                .Catalogue.All.AsValueEnumerable()
                .Select(perk => new PerkEntry
                {
                    Id = perk.Id,
                    Name = Plugin.Localized(
                        perk.Id + " name",
                        snapshot.Locale.TryGetValue(perk.Id + " name", out var name) ? name : perk.Id
                    ),
                    Description = Plugin.Localized(
                        perk.Id + " description",
                        snapshot.Locale.TryGetValue(perk.Id + " description", out var description) ? description : ""
                    ),
                    Points = perk.Points ?? 0,
                    Common = snapshot.Catalogue.Common.Contains(perk),
                    Enabled = snapshot.Rules.EnabledCommonIds.Contains(perk.Id),
                    Unavailable = snapshot.Unavailable.TryGetValue(perk.Id, out var reason) ? reason : "",
                    Conflicts = perk.Conflicts.ToArray(),
                })
                .ToArray(),
        };
    }

    private void Close()
    {
        if (Plugin.Busy || _opening)
        {
            return;
        }
        _screen?.DismissDialog();
        _screen?.Root.SetActive(false);
        _inputBlockedThrough = Time.frameCount + 1;
    }

    private async void Save()
    {
        if (Plugin.Busy || Plugin.InRaid || _screen == null || Plugin.Current == null)
        {
            return;
        }
        Plugin.Busy = true;
        var creationFlow = _screen.Page == ScreenPage.CreationPersonal;
        var created = !creationFlow;
        ClientSnapshot? completedCreation = null;
        _screen.SetBusy(true, creationFlow ? "" : "Saving campaign character...");
        try
        {
            if (creationFlow)
            {
                SetCreationLoader(true);
            }
            await Plugin.FlushPendingOperations();
            var snapshot = await Plugin.Request(
                created ? "edit" : "create",
                new Mutation
                {
                    SeasonId = _screen.SeasonId,
                    CharacterId = creationFlow ? _screen.CreationCharacterId : Plugin.Current.SelectedCharacterId,
                    OperationId = creationFlow ? _screen.CreationOperationId : "",
                    ExpectedRevision = Plugin.Current.State.Revision,
                    PerkIds = _screen.Selected.AsValueEnumerable().ToList(),
                    Nickname = _screen.Nickname,
                    Side = _screen.Side,
                    HeadId = _screen.HeadId,
                    VoiceId = _screen.VoiceId,
                }
            );
            if (creationFlow)
            {
                // Retain the created profile if switching or reloading needs a retry.
                completedCreation = snapshot;
                snapshot = await Plugin.Request("switch", new Mutation { Mode = "seasonal", CharacterId = snapshot.SelectedCharacterId });
                completedCreation = snapshot;
                await Plugin.Reload(snapshot);
                _startup = false;
                _screen.StartupSelection = false;
                _screen.SetBusy(false);
                _screen.Root.SetActive(false);
                _inputBlockedThrough = Time.frameCount + 1;
                return;
            }
            if (snapshot.ActiveMode == "seasonal")
            {
                _screen.SetMessage("Reloading your campaign character...");
                await Plugin.Reload(snapshot);
            }
            else
            {
                Plugin.Accept(snapshot);
            }
            _screen.SetBusy(false);
            _screen.SetState(Presentation(snapshot), created ? ScreenPage.Personal : ScreenPage.Characters);
            _screen.SetMessage(created ? "Your perk changes have been saved." : "Campaign character created. Select it to begin.");
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (creationFlow)
            {
                try
                {
                    // A response can be lost after the server commits creation or switching.
                    // Recover the authoritative identity before offering a retry.
                    var recovered = await Plugin.Request("snapshot");
                    if (
                        recovered
                            .Characters.AsValueEnumerable()
                            .Any(character => character.CreationOperationId == _screen.CreationOperationId && character.Exists)
                    )
                    {
                        completedCreation = recovered;
                    }
                }
                catch (Exception recoveryError)
                {
                    Plugin.Error(recoveryError);
                }
            }
            _screen.SetBusy(false);
            if (completedCreation != null)
            {
                if (
                    completedCreation.ActiveMode == "seasonal"
                    && completedCreation.SelectedCharacterId != completedCreation.EffectiveProfileId
                )
                {
                    // Creation can finish before switching while another seasonal PMC
                    // is loaded. Expose the new card without applying its modifiers.
                    Plugin.Current!.Characters = completedCreation.Characters;
                    completedCreation = Plugin.Current;
                }
                else
                {
                    Plugin.Accept(completedCreation);
                }
                _startup = true;
                _screen.StartupSelection = true;
                _screen.SetState(Presentation(completedCreation), ScreenPage.Characters);
                _screen.SetMessage("Your character was created. Select PvE Campaign to retry loading it. " + exception.Message, true);
            }
            else
            {
                _screen.SetMessage(exception.Message, true);
            }
        }
        finally
        {
            SetCreationLoader(false);
            Plugin.Busy = false;
        }
    }

    private void SetCreationLoader(bool visible)
    {
        if (visible && !_creationLoader && _canvas)
        {
            // Clone EFT's animated loading indicator into our overlay, which is above
            // PreloaderUI. This preserves native artwork without changing its loader state.
            _creationLoader = Instantiate(PreloaderUI.Instance._loader, _canvas!.transform, false);
            _creationLoader.name = "SeasonalCreationLoader";
            foreach (var graphic in _creationLoader.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }
        if (_creationLoader)
        {
            _creationLoader!.SetActive(visible);
        }
    }

    private async void Switch(string characterId)
    {
        var character = Plugin.Current?.Characters.AsValueEnumerable().FirstOrDefault(c => c.Id == characterId || c.Mode == characterId);
        if (character == null)
        {
            return;
        }

        var mode = character.Mode;
        if (Plugin.Busy || Plugin.InRaid || _screen == null)
        {
            return;
        }
        if (
            Plugin.Current?.EffectiveProfileId == character.Id
            && CharacterSession.IsLoaded(Plugin.Current, mode, Plugin.App?.Session?.Profile?.Id)
        )
        {
            _startup = false;
            _screen.StartupSelection = false;
            Close();
            return;
        }
        Plugin.Busy = true;
        _screen.SetBusy(true, "Loading " + mode + " character...");
        try
        {
            await ShowSwitchLoader(character);
            await Plugin.FlushPendingOperations();
            await Plugin.Reload(await Plugin.Request("switch", new Mutation { Mode = mode, CharacterId = character.Id }));
            _startup = false;
            _screen.StartupSelection = false;
            _screen.SetBusy(false);
            _screen.Root.SetActive(false);
            _inputBlockedThrough = Time.frameCount + 1;
        }
        catch (Exception exception)
        {
            if (_destroyed)
            {
                return;
            }
            Plugin.Error(exception);
            _screen.SetBusy(false);
            _screen.SetMessage(exception.Message + " Restart the client if reconnecting fails.", true);
        }
        finally
        {
            HideSwitchLoader();
            Plugin.Busy = false;
        }
    }

    private void LoadCharacter(string mode, RawImage target)
    {
        var visual = Plugin
            .Current?.Characters.AsValueEnumerable()
            .FirstOrDefault(character => character.Id == mode || character.Mode == mode)
            ?.Visual;
        if (visual == null)
        {
            return;
        }

        var camera = _bundle!.LoadAsset<GameObject>("assets/mods/wtt-campaigns.assets/ui/selection-camera.prefab");
        if (!camera)
        {
            return;
        }

        var font =
            _bundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf")
            ?? Resources.FindObjectsOfTypeAll<Font>().AsValueEnumerable().FirstOrDefault(value => value.name == "Jovanny Lemonad - Bender")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        target.gameObject.AddComponent<CharacterPreview>().Show(visual, camera, font);
    }

    internal void PlayInterfaceSound(InterfaceSound sound)
    {
        if (_destroyed || sound == InterfaceSound.None)
        {
            return;
        }
        try
        {
            var gui = Singleton<GUISounds>.Instance;
            switch (sound)
            {
                case InterfaceSound.ButtonHover:
                    gui.PlayUISound(EUISoundType.ButtonOver);
                    break;
                case InterfaceSound.ButtonClick:
                    gui.PlayUISound(EUISoundType.ButtonClick);
                    break;
                case InterfaceSound.Back:
                    gui.PlayUISound(EUISoundType.MenuEscape);
                    break;
                default:
                    PlayBundledSound(
                        sound == InterfaceSound.PerkOn ? "perk-on"
                        : sound == InterfaceSound.PerkOff ? "perk-off"
                        : "perk-reset"
                    );
                    break;
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    private void PlayProfileHover(bool seasonal)
    {
        PlayBundledSound(seasonal ? "profile-hover-seasonal" : "profile-hover-normal");
    }

    private void PlayBundledSound(string clipName)
    {
        if (_destroyed)
        {
            return;
        }
        try
        {
            var clip = _bundle!.LoadAsset<AudioClip>("assets/mods/wtt-campaigns.assets/audio/" + clipName + ".wav");
            if (!clip)
            {
                throw new InvalidDataException("Missing bundled interface sound: " + clipName);
            }
            Singleton<GUISounds>.Instance.PlaySound(clip);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    internal void LoadArtwork(string artworkName, Image target)
    {
        try
        {
            _bundle ??=
                AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_ui.bundle"))
                ?? throw new InvalidDataException("Missing campaign UI bundle.");
            var assetPath = artworkName.StartsWith("hub:", StringComparison.Ordinal)
                ? "hubartwork/" + artworkName.Substring(4)
                : "selectionartwork/" + artworkName;
            var sprite = _bundle.LoadAsset<Sprite>("assets/mods/wtt-campaigns.assets/" + assetPath + ".png");
            if (!sprite)
            {
                throw new InvalidDataException("Missing bundled selection artwork: " + artworkName);
            }
            if (!_destroyed && target)
            {
                target.sprite = sprite;
                target.enabled = true;
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    internal async void LoadIcon(string id, Image target)
    {
        var path = SeasonImageLoader.PathFor("icons", id);
        try
        {
            if (!_images.TryGetValue(path, out var task))
            {
                _images[path] = task = FetchIcon(path);
            }
            var sprite = await task;
            if (!_destroyed && target)
            {
                target.sprite = sprite;
                target.color = Color.white;
                target.preserveAspect = true;
                target.enabled = true;
            }
        }
        catch (Exception exception)
        {
            _images.Remove(path);
            Plugin.Error(exception);
        }
    }

    private async Task<Sprite> FetchIcon(string path)
    {
        var texture = await SeasonImageLoader.LoadAsync(path);
        if (_destroyed)
        {
            Destroy(texture);
            throw new OperationCanceledException("Campaign UI closed.");
        }
        return Sprite.Create(texture, new Rect(0, 0, 272, 272), new Vector2(.5f, .5f));
    }

    private void OnDestroy()
    {
        _destroyed = true;
        HideSwitchLoader();
        _screen?.Dispose();
        if (_canvas)
        {
            Destroy(_canvas);
        }
        foreach (var task in _images.Values.AsValueEnumerable().Where(task => task.Status == TaskStatus.RanToCompletion))
        {
            Destroy(task.Result.texture);
            Destroy(task.Result);
        }
        _images.Clear();
        if (_bundle)
        {
            _bundle!.Unload(false);
        }
    }
}
