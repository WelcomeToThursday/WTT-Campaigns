using EFT.InputSystem;
using EFT.UI;
using EFT.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

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
        panel.style.width = 500;
        panel.style.maxWidth = Length.Percent(92);
        panel.style.maxHeight = Length.Percent(85);
        shield.Add(panel);
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.flexShrink = 0;
        header.style.marginBottom = 12;
        var heading = new Label(title) { enableRichText = false, pickingMode = PickingMode.Ignore };
        heading.style.fontSize = 18;
        heading.style.flexGrow = 1;
        heading.style.flexShrink = 1;
        heading.style.minWidth = 0;
        heading.style.whiteSpace = WhiteSpace.Normal;
        header.Add(heading);
        var close = new Button(() => DismissPicker()) { text = "×", tooltip = "Close selection (Escape)" };
        close.style.width = 32;
        close.style.height = 32;
        close.style.flexShrink = 0;
        close.style.marginTop = close.style.marginBottom = close.style.marginRight = 0;
        header.Add(close);
        panel.Add(header);
        var rows = new List<(string Id, string Name)>();
        foreach (var choice in choices)
            rows.Add(choice);
        var list = new ListView
        {
            itemsSource = rows,
            fixedItemHeight = 44,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            selectionType = SelectionType.Single,
            makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 40;
                row.style.marginTop = row.style.marginBottom = 2;
                row.style.paddingLeft = row.style.paddingRight = 10;
                var marker = new Label { name = "selected", pickingMode = PickingMode.Ignore };
                marker.style.width = 24;
                marker.style.flexShrink = 0;
                var name = new Label
                {
                    name = "name",
                    enableRichText = false,
                    pickingMode = PickingMode.Ignore,
                };
                name.style.flexGrow = 1;
                name.style.minWidth = 0;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                // Override the modal's generic list label padding so both
                // labels share a centered baseline inside the fixed row.
                foreach (var label in new[] { marker, name })
                    label.style.paddingTop = label.style.paddingBottom = label.style.paddingLeft = label.style.paddingRight = 0;
                row.Add(marker);
                row.Add(name);
                return row;
            },
            bindItem = (element, index) =>
            {
                var current = rows[index].Id == selected;
                element.Q<Label>("name").text = rows[index].Name;
                element.Q<Label>("selected").text = current ? "›" : "";
                element.tooltip = rows[index].Name;
                element.EnableInClassList("editor-selected", current);
            },
        };
        list.style.height = Math.Min(8, rows.Count) * 44;
        list.style.flexShrink = 1;
        list.style.minHeight = 0;
        EditorScrollStyle.Apply(list.Q<ScrollView>());
        panel.Add(list);
        if (rows.Count == 0)
        {
            var empty = new Label("No choices available.");
            empty.style.paddingTop = empty.style.paddingBottom = 12;
            panel.Add(empty);
        }
        var selectedIndex = rows.FindIndex(row => row.Id == selected);
        if (selectedIndex >= 0)
        {
            // Highlight the saved choice without preselecting the ListView;
            // clicking that same choice must still select it and close.
            list.schedule.Execute(() => list.ScrollToItem(selectedIndex));
        }
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
