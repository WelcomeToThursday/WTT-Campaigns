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
                throw new InvalidOperationException("The editor screen is already registered.");
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
        _document = new EditorToolkitDocument("Editor Home", 32100);
        _document.Content.AddToClassList("editor-home-background");
        _document.Content.pickingMode = PickingMode.Position;
        _document.Tick = Fit;
        _document.Escape = () =>
        {
            if (DismissPicker())
                _dismissFrame = Time.frameCount;
        };
        _stage = _document.Clone<VisualElement>("Home");
        _document.Content.Add(_stage);
        foreach (var button in _stage.Query<Button>().ToList())
            _buttons.Add(button.name, button);
        foreach (var label in _stage.Query<Label>().ToList())
            if (!string.IsNullOrEmpty(label.name))
                _labels.Add(label.name, label);
    }

    internal void Button(string name, Action action) => _buttons[name].clicked += action;

    internal void Interactable(string name, bool value) => _buttons[name].SetEnabled(value);

    internal void SelectedTab(string name, bool value) => _buttons[name].EnableInClassList("editor-active-tab", value);

    internal void Visible(string name, bool value) => _stage.Q(name).style.display = value ? DisplayStyle.Flex : DisplayStyle.None;

    internal string LevelName => _stage.Q<TextField>("EditorLevelName").value;

    internal void ResetLevelName() => _stage.Q<TextField>("EditorLevelName").SetValueWithoutNotify("New level");

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
        var shield = _document.Clone<VisualElement>("HomePicker");
        _picker = shield;
        _document.Content.Add(shield);
        shield.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == shield)
                DismissPicker();
        });
        shield.Q<Label>("Heading").text = title;
        shield.Q<Button>("Close").clicked += () => DismissPicker();
        var rows = new List<(string Id, string Name)>();
        foreach (var choice in choices)
            rows.Add(choice);
        var list = shield.Q<ListView>("Choices");
        list.fixedItemHeight = 44;
        list.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        list.selectionType = SelectionType.Single;
        list.makeItem = () => _document.Clone<VisualElement>("PickerRow");
        list.bindItem = (element, index) =>
        {
            var current = rows[index].Id == selected;
            element.Q<Label>("name").text = rows[index].Name;
            element.Q<Label>("selected").text = current ? "›" : "";
            element.tooltip = rows[index].Name;
            element.EnableInClassList("editor-selected", current);
        };
        list.itemsSource = rows;
        list.style.height = Math.Min(8, rows.Count) * 44;
        EditorScrollStyle.Apply(list.Q<ScrollView>());
        shield.Q<Label>("Empty").style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
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
