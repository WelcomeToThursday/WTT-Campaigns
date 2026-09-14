using EFT.InputSystem;
using EFT.UI;
using EFT.UI.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring;

// Registered with EFT's screen manager; no independent overlay canvas or raid-editor prefab.
public sealed class EditorHomeScreen : EftScreen<EditorHomeScreen.Controller, EditorHomeScreen>
{
    internal const EEftScreenType ScreenType = (EEftScreenType)0x575454;

    public sealed class Controller : EftScreenManager.EftScreenController<Controller, EditorHomeScreen>
    {
        public override EEftScreenType ScreenType => EditorHomeScreen.ScreenType;
        public override bool KeyScreen => true;
        public override bool MainEnvironment => true;
        public override EStateSwitcher MenuChatBarVisibility => EStateSwitcher.Disabled;
        public override EStateSwitcher TaskBarButtonsAvailability => EStateSwitcher.Disabled;
        public override EStateSwitcher ShowEnvironment => EStateSwitcher.Enabled;
        public override EStateSwitcher ShowEnvironmentCamera => EStateSwitcher.Enabled;
        public override EStateSwitcher EnvironmentOverlay => EStateSwitcher.Enabled;
        public override EStateSwitcher CameraBlur => EStateSwitcher.Enabled;
        public override EShadingStateSwitcher ShadingType => EShadingStateSwitcher.Default;
    }

    private readonly Dictionary<string, DefaultUIButton> _buttons = new();
    private readonly Dictionary<string, TextMeshProUGUI> _labels = new();
    private RectTransform _stage = null!;
    private DefaultUIButton _template = null!;
    private GameObject? _picker;
    internal bool IsOpen => this && gameObject.activeInHierarchy;

    internal static EditorHomeScreen Create(MenuScreen menu)
    {
        var root = UiElements.Rect("CampaignEditorScreen", menu.transform.parent, 0, 0);
        UiElements.Stretch(root);
        root.gameObject.SetActive(false);
        // Native menus can share a rendering canvas while owning separate raycasters.
        // Always give this screen a canvas/raycaster pair: closing the native menu
        // must not remove the component responsible for receiving our pointer events.
        var parentCanvas = root.GetComponentInParent<Canvas>();
        var sourceCanvas = menu.GetComponentInParent<Canvas>() ?? menu.GetComponentInChildren<Canvas>(true);
        if (!sourceCanvas)
        {
            UnityEngine.Object.Destroy(root.gameObject);
            throw new InvalidOperationException("The native menu canvas is unavailable.");
        }
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.worldCamera = sourceCanvas.worldCamera ? sourceCanvas.worldCamera : sourceCanvas.rootCanvas.worldCamera;
        if (!parentCanvas)
        {
            canvas.renderMode = sourceCanvas.renderMode;
            canvas.planeDistance = sourceCanvas.planeDistance;
            canvas.sortingLayerID = sourceCanvas.sortingLayerID;
            canvas.sortingOrder = sourceCanvas.sortingOrder;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            if (sourceCanvas.GetComponent<CanvasScaler>() is { } sourceScaler)
            {
                scaler.uiScaleMode = sourceScaler.uiScaleMode;
                scaler.referenceResolution = sourceScaler.referenceResolution;
                scaler.screenMatchMode = sourceScaler.screenMatchMode;
                scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                scaler.scaleFactor = sourceScaler.scaleFactor;
                scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            }
        }
        var raycaster = root.gameObject.AddComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        var screen = root.gameObject.AddComponent<EditorHomeScreen>();
        screen.Build(menu._playerButton);
        var manager = EftScreenManager.Instance;
        if (manager.TryGetScreen(ScreenType, out var existing) && existing)
        {
            UnityEngine.Object.Destroy(root.gameObject);
            throw new InvalidOperationException("The Campaign editor screen is already registered.");
        }
        manager.RegisterScreen(ScreenType, screen);
        return screen;
    }

    public override void Show(Controller controller)
    {
        ShowGameObject();
        CanvasGroup.interactable = true;
        CanvasGroup.blocksRaycasts = true;
        transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        Fit();
    }

    public override ETranslateResult TranslateCommand(ECommand command) => ETranslateResult.BlockAll;

    public override void TranslateAxes(ref float[] axes)
    {
        if (axes != null)
            Array.Clear(axes, 0, axes.Length);
    }

    public override void Close()
    {
        DismissPicker();
        base.Close();
    }

    public override void OnDestroy()
    {
        EftScreenManager.Instance.ReleaseScreen(ScreenType, this);
        base.OnDestroy();
    }

    internal void Fit()
    {
        var size = ((RectTransform)transform).rect.size;
        _stage.localScale = Vector3.one * Mathf.Min(Mathf.Max(1, size.y / 1080), Mathf.Min(size.x / 1280, size.y / 720));
    }

    private void Build(DefaultUIButton source)
    {
        UiElements.Fill((RectTransform)transform, new Color(0, 0, 0, .48f), true);
        _stage = UiElements.Rect("EditorSafeArea", transform, 1280, 720);
        _template = UnityEngine.Object.Instantiate(source, _stage, false);
        _template.name = "NativeButtonTemplate";
        _template.gameObject.SetActive(false);

        foreach (var item in WTT.Campaigns.UI.Screens.EditorHomeComposition.Elements)
        {
            if (item.Kind == "Panel")
            {
                var panel = Panel(
                    _stage,
                    item.Id,
                    item.X,
                    item.Y,
                    item.Width,
                    item.Height,
                    item.Id.EndsWith("TitleBar") ? EditorTarkovTheme.Container : EditorTarkovTheme.Surface
                );
                EditorTarkovTheme.Frame(panel);
            }
            else if (item.Kind == "Button")
                AddButton(item.Id, item.Text, item.X, item.Y, item.Width, item.Height);
            else
                Label(item.Id, item.Text, item.Size, item.X, item.Y, item.Width, item.Height, EditorTarkovTheme.Ink);
        }
        Panel(_stage, "ColumnRule", 438, 124, 1, 404, EditorTarkovTheme.Border);
        Rule(124, 580, 1028);
    }

    private static RectTransform Place(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = UiElements.Rect(name, parent, width, height);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        return rect;
    }

    private static RectTransform Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var rect = Place(parent, name, x, y, width, height);
        UiElements.Fill(rect, color, true);
        return rect;
    }

    private void Rule(float x, float y, float width) => Panel(_stage, "Separator", x, y, width, 1, new Color(.55f, .52f, .43f, .5f));

    private TextMeshProUGUI Label(string name, string text, int size, float x, float y, float width, float height, Color? color = null)
    {
        var label = Place(_stage, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _template._headerLabel.font;
        label.fontSharedMaterial = _template._headerLabel.fontSharedMaterial;
        label.fontSize = size;
        label.color = color ?? UiElements.Ink;
        label.richText = false;
        label.text = text;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        _labels.Add(name, label);
        return label;
    }

    private DefaultUIButton NativeButton(Transform parent, string name, string text, float x, float y, float width, float height)
    {
        var button = UnityEngine.Object.Instantiate(_template, parent, false);
        button.name = name;
        button.OnClick.RemoveAllListeners();
        button.OnMouseOver.RemoveAllListeners();
        button.OnMouseOut.RemoveAllListeners();
        button.SetIcon(null);
        button.SetTooltips("", "");
        button._minWidth = -1;
        button._useEllipsis = true;
        button.SetRawText(text, 18);
        button._headerLabel.richText = false;
        if (button._sizeLabel)
            button._sizeLabel.richText = false;
        button._headerLabel.overflowMode = TextOverflowModes.Ellipsis;
        if (button.GetComponent<ContentSizeFitter>() is { } fitter)
            fitter.enabled = false;
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        button.Interactable = true;
        // The source prefab's visible graphics need not accept raycasts. Supply a
        // transparent hit area owned by the clone, preserving its native artwork.
        var hit = UiElements.Rect("EditorButtonHitArea", button.transform, 0, 0);
        UiElements.Stretch(hit);
        UiElements.Fill(hit, Color.clear, true);
        button.CanvasGroup.interactable = true;
        button.CanvasGroup.blocksRaycasts = true;
        button.gameObject.SetActive(true);
        return button;
    }

    private void AddButton(string name, string text, float x, float y, float width, float height) =>
        _buttons.Add(name, NativeButton(_stage, name, text, x, y, width, height));

    internal void Button(string name, Action action) => _buttons[name].OnClick.AddListener(() => action());

    internal void Interactable(string name, bool value)
    {
        if (_buttons[name].Interactable != value)
            _buttons[name].Interactable = value;
    }

    internal void Visible(string name, bool value) => _buttons[name].gameObject.SetActive(value);

    internal void Text(string name, string value)
    {
        if (_labels[name].text != value)
            _labels[name].text = value;
    }

    internal void Caption(string name, string value)
    {
        if (_buttons[name].HeaderText != value)
            _buttons[name].SetRawText(value, 18);
    }

    internal bool DismissPicker()
    {
        if (!_picker)
            return false;
        _picker!.SetActive(false);
        UnityEngine.Object.Destroy(_picker);
        _picker = null;
        return true;
    }

    internal void Choose(string title, IReadOnlyList<(string Id, string Name)> choices, string selected, Action<string> choose)
    {
        DismissPicker();
        var shield = Panel(_stage, "ChoiceShield", 0, 0, 1280, 720, new Color(0, 0, 0, .7f));
        _picker = shield.gameObject;
        var dismiss = shield.gameObject.AddComponent<Button>();
        dismiss.onClick.AddListener(() => DismissPicker());
        var panel = Panel(shield, "ChoicePanel", 280, 84, 720, 552, new Color(.045f, .045f, .04f, 1));
        EditorTarkovTheme.Frame(panel);
        var heading = NativeButton(panel, "ChoiceHeading", title + "   /   CLOSE", 12, 8, 696, 32);
        heading.OnClick.AddListener(() => DismissPicker());
        var viewport = Place(panel, "ChoicesViewport", 12, 52, 696, 484);
        UiElements.Fill(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Place(viewport, "Choices", 0, 0, 680, Math.Max(484, choices.Count * 42));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36;
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var button = NativeButton(content, "Choice", (choice.Id == selected ? "•  " : "") + choice.Name, 0, index * 42, 680, 36);
            button.OnClick.AddListener(() =>
            {
                DismissPicker();
                choose(choice.Id);
            });
        }
        // A visible thumb also makes long lists discoverable without a mouse wheel.
        var track = Panel(viewport, "Scrollbar", 780, 0, 12, 576, new Color(1, 1, 1, .08f));
        var thumb = Panel(track, "Thumb", 0, 0, 12, 576, UiElements.Muted);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.targetGraphic = thumb.GetComponent<Image>();
        scrollbar.handleRect = thumb;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalNormalizedPosition = 1;
    }
}
