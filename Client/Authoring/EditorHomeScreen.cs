using EFT.InputSystem;
using EFT.UI;
using EFT.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring;

// Native EFT navigation owns the screen; Toolkit owns every visible control.
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

    private EditorToolkitDocument _document = null!;
    private VisualElement _stage = null!;
    private VisualElement? _picker;
    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<string, Label> _labels = new();
    private int _dismissFrame = -1;
    internal bool IsOpen => this && gameObject.activeInHierarchy;

    internal static EditorHomeScreen Create(MenuScreen menu)
    {
        var root = new GameObject("CampaignEditorScreen", typeof(RectTransform));
        root.transform.SetParent(menu.transform.parent, false);
        root.SetActive(false);
        var screen = root.AddComponent<EditorHomeScreen>();
        try
        {
            screen.Build();
            var manager = EftScreenManager.Instance;
            if (manager.TryGetScreen(ScreenType, out var existing) && existing)
                throw new InvalidOperationException("The Campaign editor screen is already registered.");
            manager.RegisterScreen(ScreenType, screen);
            return screen;
        }
        catch
        {
            screen._document?.Dispose();
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    public override void Show(Controller controller)
    {
        ShowGameObject();
        transform.SetAsLastSibling();
        _document.SetVisible(true);
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
        _document?.SetVisible(false);
        base.Close();
    }

    public override void OnDestroy()
    {
        _document?.Dispose();
        EftScreenManager.Instance.ReleaseScreen(ScreenType, this);
        base.OnDestroy();
    }

    internal void Fit()
    {
        if (_document == null)
            return;
        var scale = Mathf.Min(_document.Width / 1280, _document.Height / 720);
        _stage.style.scale = new Scale(new Vector3(scale, scale, 1));
        _stage.style.left = (_document.Width - 1280) / 2;
        _stage.style.top = (_document.Height - 720) / 2;
    }

    private void Build()
    {
        _document = new EditorToolkitDocument("Campaign Editor Home", 32100);
        _document.Content.style.backgroundColor = new Color(0, 0, 0, .48f);
        _document.Content.pickingMode = PickingMode.Position;
        _document.Tick = Fit;
        _document.Escape = () =>
        {
            if (DismissPicker())
                _dismissFrame = Time.frameCount;
        };
        _stage = new VisualElement();
        _stage.AddToClassList("editor-home-stage");
        _document.Content.Add(_stage);
        foreach (var item in WTT.Campaigns.UI.Screens.EditorHomeComposition.Elements)
        {
            VisualElement element;
            if (item.Kind == "Button")
            {
                var button = new Button { text = item.Text };
                _buttons.Add(item.Id, button);
                element = button;
            }
            else if (item.Kind == "Panel")
            {
                element = new VisualElement();
                element.AddToClassList("editor-home-panel");
            }
            else
            {
                var label = new Label(item.Text) { enableRichText = false, pickingMode = PickingMode.Ignore };
                label.style.fontSize = item.Size;
                label.style.whiteSpace = WhiteSpace.Normal;
                _labels.Add(item.Id, label);
                element = label;
            }
            element.name = item.Id;
            element.style.position = Position.Absolute;
            element.style.left = item.X;
            element.style.top = item.Y;
            element.style.width = item.Width;
            element.style.height = item.Height;
            _stage.Add(element);
        }
    }

    internal void Button(string name, Action action) => _buttons[name].clicked += action;

    internal void Interactable(string name, bool value) => _buttons[name].SetEnabled(value);

    internal void Visible(string name, bool value) => _buttons[name].style.display = value ? DisplayStyle.Flex : DisplayStyle.None;

    internal void Text(string name, string value)
    {
        if (_labels[name].text != value)
            _labels[name].text = value;
    }

    internal void Caption(string name, string value)
    {
        if (_buttons[name].text != value)
            _buttons[name].text = value;
    }

    internal bool DismissPicker()
    {
        if (_picker == null)
            return _dismissFrame == Time.frameCount;
        _picker.RemoveFromHierarchy();
        _picker = null;
        return true;
    }

    internal void Choose(string title, IReadOnlyList<(string Id, string Name)> choices, string selected, Action<string> choose)
    {
        DismissPicker();
        var shield = new VisualElement();
        shield.AddToClassList("editor-modal-shield");
        _picker = shield;
        _document.Content.Add(shield);
        shield.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == shield)
                DismissPicker();
        });
        var panel = new VisualElement();
        panel.AddToClassList("editor-modal");
        shield.Add(panel);
        panel.Add(new Button(() => DismissPicker()) { text = title + " / CLOSE" });
        var rows = new List<(string Id, string Name)>();
        foreach (var choice in choices)
            rows.Add(choice);
        var list = new ListView
        {
            itemsSource = rows,
            fixedItemHeight = 38,
            selectionType = SelectionType.Single,
            makeItem = () => new Label { enableRichText = false },
            bindItem = (element, index) =>
            {
                ((Label)element).text = rows[index].Name;
                element.EnableInClassList("editor-selected", rows[index].Id == selected);
            },
        };
        list.style.height = Mathf.Min(460, _document.Height - 160);
        panel.Add(list);
        list.selectionChanged += items =>
        {
            foreach (var item in items)
            {
                var choice = ((string Id, string Name))item;
                DismissPicker();
                choose(choice.Id);
                break;
            }
        };
    }
}
