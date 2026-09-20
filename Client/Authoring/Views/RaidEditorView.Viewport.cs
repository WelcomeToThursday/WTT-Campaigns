using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    internal readonly EditorViewportState ViewportState = new();
    private VisualElement _viewportToolbar = null!;
    private VisualElement _viewportMenu = null!;
    private ConfigEntry<int>? _overlayPreferences;
    internal bool ViewportMenuOpen => _viewportMenu != null && _viewportMenu.style.display.value == DisplayStyle.Flex;

    private void BuildViewportToolbar(VisualElement workspace)
    {
        _viewportToolbar = Document.Clone<VisualElement>("ViewportToolbar");
        workspace.Add(_viewportToolbar);
        foreach (var button in _viewportToolbar.Query<Button>().ToList())
            Register(button.name, new EditorButton(button));
        var speed = _viewportToolbar.Q<TextField>("CameraSpeed");
        var speedInput = new EditorInput(speed);
        Register("CameraSpeed", speedInput);
        EditorControlLayout.Field(speed, false);
        speedInput.NumericDrag = EditorNumericDrag.Attach(speed, "CameraSpeed");
        var scroll = _viewportToolbar.Q<ScrollView>("ViewportTools");
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        _viewportMenu = _viewportToolbar.Q("ViewportOverlayMenu");
        _viewportMenu.style.display = DisplayStyle.None;
        _overlayPreferences = Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Viewport overlays",
            (int)EditorOverlays.All,
            "Visible viewport overlays: zones=1, routes=2, AI=4, bounds=8, handles=16. Combined by addition."
        );
        ViewportState.Overlays = (EditorOverlays)_overlayPreferences.Value & EditorOverlays.All;
        foreach (
            var (name, flag) in new[]
            {
                ("OverlayZones", EditorOverlays.Zones),
                ("OverlayRoutes", EditorOverlays.Routes),
                ("OverlayAi", EditorOverlays.Ai),
                ("OverlayBounds", EditorOverlays.Bounds),
                ("OverlayHandles", EditorOverlays.Handles),
            }
        )
        {
            var toggle = _viewportMenu.Q<Toggle>(name);
            toggle.SetValueWithoutNotify((ViewportState.Overlays & flag) != 0);
            toggle.RegisterValueChangedCallback(evt =>
            {
                ViewportState.Overlays = evt.newValue ? ViewportState.Overlays | flag : ViewportState.Overlays & ~flag;
                if (ViewportState.Clean)
                    ToggleCleanView();
                try
                {
                    _overlayPreferences.Value = (int)ViewportState.Overlays;
                    if (!Plugin.Instance.Config.SaveOnConfigSet)
                        Plugin.Instance.Config.Save();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Plugin.Error(error);
                }
            });
        }
        Button(
            "ViewportOverlays",
            () =>
            {
                var open = !ViewportMenuOpen;
                DismissDropdowns();
                Windows.DismissMenus();
                _viewportMenu.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open)
                    _viewportToolbar.BringToFront();
            }
        );
        Button("ViewportOverlayClose", () => DismissViewportMenu());
        Button("ViewportClean", ToggleCleanView);
        Button("ViewportMaximize", () => Windows.SetViewportMaximized(!Windows.ViewportMaximized));
        _viewportToolbar
            .Q<Label>("ViewportTitle")
            .RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount == 2)
                {
                    Windows.SetViewportMaximized(!Windows.ViewportMaximized);
                    evt.StopPropagation();
                }
            });
    }

    internal void ToggleCleanView()
    {
        ViewportState.Clean = !ViewportState.Clean;
        Caption("ViewportClean", ViewportState.Clean ? "Show overlays" : "Clean");
        Highlight("ViewportClean", ViewportState.Clean);
    }

    internal void ShowNavigationOverlay()
    {
        if (ViewportState.Clean)
            ToggleCleanView();
        _viewportMenu.Q<Toggle>("OverlayAi").value = true;
    }

    private bool DismissViewportMenu()
    {
        if (!ViewportMenuOpen)
            return false;
        _viewportMenu.style.display = DisplayStyle.None;
        _dropdownDismissFrame = Time.frameCount;
        return true;
    }
}
