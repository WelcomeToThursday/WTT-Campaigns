using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// One retained visual tree, with no Canvas, Graphic, or hidden uGUI controls.
internal sealed class EditorToolkitDocument : IDisposable
{
    private static AssetBundle? _bundle;

    // Shared assets live for the client session. Never synchronously unload them
    // from a screen's OnDestroy while Unity is changing scenes.
    private static PanelSettings? _template;
    private static VisualTreeAsset? _tree;
    private static readonly Dictionary<string, VisualTreeAsset> Templates = new();
    private static Font? _font;
    private static Shader? _previewShader;
    private static Shader? _viewportShader;
    private bool _disposed;
    internal readonly GameObject Host;
    internal readonly PanelSettings Settings;
    internal readonly VisualElement Root;
    internal readonly VisualElement Content;
    internal bool Visible { get; private set; }
    internal Action? Tick;
    internal Action? Escape;
    internal Action? CancelTyping;
    internal int EscapeFrame = -1;
    internal int ScalePercent = 100;
    internal float Scale => Settings.scale;
    internal float Width => Screen.width / Scale;
    internal float Height => Screen.height / Scale;

    internal EditorToolkitDocument(string name, int order)
    {
        EnsureAssets();
        Plugin.LogInfo("Editor Toolkit: creating " + name);
        try
        {
            Settings = UnityEngine.Object.Instantiate(_template!);
            Settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            Settings.sortingOrder = order;
            Host = new GameObject(name);
            // Native owners dispose their documents; scene unload must not destroy
            // a still-owned UIDocument behind those owners' backs.
            UnityEngine.Object.DontDestroyOnLoad(Host);
            var document = Host.AddComponent<UIDocument>();
            document.panelSettings = Settings;
            document.visualTreeAsset = _tree!;
            Root = document.rootVisualElement;
            Root.pickingMode = PickingMode.Ignore;
            Content = Root.Q("surface");
            Content.ClearClassList();
            Content.AddToClassList("editor-root");
            Content.pickingMode = PickingMode.Ignore;
            Content.style.position = Position.Absolute;
            Content.style.left = Content.style.top = Content.style.right = Content.style.bottom = 0;
            Content.style.unityFontDefinition = FontDefinition.FromFont(_font!);
            Root.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode != KeyCode.Escape)
                        return;
                    if (EscapeFrame == Time.frameCount)
                    {
                        evt.StopPropagation();
                        evt.PreventDefault();
                        return;
                    }
                    if (Typing)
                    {
                        EscapeFrame = Time.frameCount;
                        if (CancelTyping != null)
                            CancelTyping();
                        else
                            ReleaseFocus();
                        evt.StopPropagation();
                        evt.PreventDefault();
                    }
                    else
                        Escape?.Invoke();
                },
                TrickleDown.TrickleDown
            );
            Host.AddComponent<EditorToolkitTicker>().Tick = Update;
            SetVisible(false);
            Update();
            Plugin.LogInfo("Editor Toolkit: ready " + name);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal Shader PreviewShader => _previewShader!;
    internal Shader ViewportShader => _viewportShader!;

    // Detach the authored root: a TemplateContainer would change the existing
    // docking and direct-child layout contracts.
    internal T Clone<T>(string template)
        where T : VisualElement => CloneTemplate<T>(template);

    internal static T CloneTemplate<T>(string template)
        where T : VisualElement
    {
        EnsureAssets();
        var container = Templates[template].CloneTree();
        if (container.childCount != 1 || container[0] is not T root)
            throw new InvalidOperationException("Invalid Editor Toolkit template: " + template);
        root.RemoveFromHierarchy();
        return root;
    }

    private static void EnsureAssets()
    {
        if (_template && _tree && _font && _previewShader && _viewportShader && Templates.Count == 49)
            return;
        Plugin.LogInfo("Editor Toolkit: loading shared assets");
        const string path = "assets/mods/wtt-campaigns.assets/editortoolkit/";
        if (!_bundle)
            _bundle =
                AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_editor_toolkit.bundle"))
                ?? throw new InvalidOperationException("Install the matching Editor Toolkit bundle.");
        var shaders = _bundle!.LoadAllAssets<Shader>();
        if (shaders.Length < 3)
            throw new InvalidOperationException("Editor Toolkit shaders are missing.");
        foreach (var shader in shaders)
            if (!shader.isSupported)
                throw new InvalidOperationException("Unsupported editor shader: " + shader.name);
        _template = _bundle.LoadAsset<PanelSettings>(path + "editorpanel.asset");
        _tree = _bundle.LoadAsset<VisualTreeAsset>(path + "editor.uxml");
        _font = _bundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
        _previewShader = _bundle.LoadAsset<Shader>("assets/mods/wtt-campaigns.assets/raideditor/campaignscenepreview.shader");
        _viewportShader = _bundle.LoadAsset<Shader>(path + "viewportcopy.shader");
        if (!_template || !_tree || !_font || !_previewShader || !_viewportShader)
            throw new InvalidOperationException("Editor Toolkit assets are incomplete.");
        Templates.Clear();
        foreach (
            var name in new[]
            {
                "Action",
                "BrowserRow",
                "CampaignTest",
                "CaptureTask",
                "CategoryRail",
                "ChoiceField",
                "ChoiceOption",
                "ChoicePopup",
                "FieldMessage",
                "InspectorSection",
                "InspectorHeader",
                "ConflictRow",
                "ConflictShield",
                "ContextMenu",
                "Console",
                "ConsoleRow",
                "Controls",
                "DockDivider",
                "DockTab",
                "DockTabs",
                "DropPreview",
                "EditorWalkStatus",
                "EnvironmentMenu",
                "Field",
                "Home",
                "HomePicker",
                "Inspector",
                "Library",
                "LootConfiguration",
                "MenuShield",
                "PickerRow",
                "RouteCaption",
                "RouteLegend",
                "Row",
                "SceneActionGroup",
                "ScopedActions",
                "StatusBar",
                "ToolbarIcon",
                "ToolbarScroll",
                "ToolbarSeparator",
                "Tooltip",
                "TransformToolbar",
                "TreeRow",
                "Window",
                "WindowsMenu",
                "Workspace",
                "WorkspaceTitleBar",
                "GameViewport",
                "ViewportToolbar",
            }
        )
        {
            var tree = _bundle.LoadAsset<VisualTreeAsset>(path + name.ToLowerInvariant() + ".uxml");
            if (!tree)
                throw new InvalidOperationException("Install the matching Editor Toolkit templates: " + name);
            Templates.Add(name, tree);
        }
        Plugin.LogInfo("Editor Toolkit: shared assets ready");
    }

    private void Update()
    {
        if (_disposed)
            return;
        Settings.scale = EditorUiScale.Resolve(Screen.width, Screen.height, ScalePercent);
        if (Visible)
            Tick?.Invoke();
    }

    internal void SetVisible(bool visible)
    {
        if (!visible)
            ReleaseFocus();
        Visible = visible;
        Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    internal bool Typing
    {
        get
        {
            if (EscapeFrame == Time.frameCount)
                return true;
            if (!Visible)
                return false;
            var element = Root.panel?.focusController.focusedElement as VisualElement;
            while (element != null)
            {
                if (element is TextField or Slider)
                    return true;
                element = element.parent;
            }
            return false;
        }
    }

    internal void ReleaseFocus() => (Root?.panel?.focusController.focusedElement as VisualElement)?.Blur();

    internal Vector2 Pointer =>
        RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
    internal bool PointerOver
    {
        get
        {
            if (!Visible || Root.panel == null)
                return false;
            var picked = Root.panel.Pick(Pointer);
            return picked != null && picked != Root && picked != Content;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Tick = null;
        Escape = null;
        CancelTyping = null;
        ReleaseFocus();
        if (Host)
        {
            Host.SetActive(false);
            UnityEngine.Object.Destroy(Host);
        }
        if (Settings)
            UnityEngine.Object.Destroy(Settings);
    }
}

[DefaultExecutionOrder(32001)]
internal sealed class EditorToolkitTicker : MonoBehaviour
{
    internal Action? Tick;

    private void LateUpdate() => Tick?.Invoke();
}
