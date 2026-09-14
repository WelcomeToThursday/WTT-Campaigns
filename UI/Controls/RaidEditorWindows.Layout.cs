using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Controls;

public sealed partial class RaidEditorWindows
{
    public Action? LayoutChanged;
    private bool _recordDetails,
        _mapDetails,
        _recordAvailable,
        _identityAvailable;
    private readonly List<(RectTransform Rect, Vector2 Position, Vector2 Size, RectTransform Row)> _propertyGeometry = new();
    private float _contentWidth = -1,
        _propertyWidth = -1;
    private int _contentMask = -1;
    private readonly string[] _categories = { "Maps", "Routes", "Zones", "Bindings", "Captures", "Scene" };

    public bool Interacting
    {
        get
        {
            foreach (var panel in _panels.Values)
                if (panel.Drag.Dragging || panel.Resize.Dragging)
                    return true;
            return false;
        }
    }

    private void RegisterWindow(string id, string title, string close, Vector2 minimum)
    {
        var panel = new Panel
        {
            Rect = (RectTransform)_controls[id],
            Minimum = minimum,
            Floating = true,
        };
        panel.Drag = _controls[title].gameObject.AddComponent<EditorWindowDrag>();
        panel.Drag.Window = panel.Rect;
        panel.Drag.Boundary = _root;
        panel.Drag.Focus = () => FocusWindow(panel.Rect);
        panel.Drag.Completed = () => LayoutChanged?.Invoke();
        var handle = UiElements.Rect(id + "Resize", panel.Rect, 20, 20);
        handle.anchorMin = handle.anchorMax = new Vector2(1, 0);
        handle.anchoredPosition = new Vector2(-10, 10);
        UiElements.Fill(handle, Color.clear, true);
        var ui = new UiElements(_controls["WorkspaceTitle"].GetComponent<Text>().font);
        var mark = ui.Label(handle, id + "ResizeMark", "◢", 16, 18, 18);
        mark.color = EditorTarkovTheme.Muted;
        mark.alignment = TextAnchor.LowerRight;
        panel.Resize = handle.gameObject.AddComponent<EditorWindowResize>();
        panel.Resize.Window = panel.Rect;
        panel.Resize.Boundary = _root;
        panel.Resize.Minimum = minimum;
        panel.Resize.Changed = FitContents;
        panel.Resize.Completed = () => LayoutChanged?.Invoke();
        var focus = panel.Rect.gameObject.AddComponent<EditorWindowFocus>();
        focus.Focus = () => FocusWindow(panel.Rect);
        _panels.Add(id, panel);
        Bind(close, () => ShowPanel(id, false));
    }

    private void FocusWindow(RectTransform panel)
    {
        if (_controls["ConflictShield"].gameObject.activeSelf)
            return;
        panel.SetAsLastSibling();
        _controls["MenuLayer"].SetAsLastSibling();
        _controls["EditorTooltip"].SetAsLastSibling();
        KeepModalOnTop();
    }

    private void ToggleWindow(string id)
    {
        DismissMenus();
        ShowPanel(id, !_panels[id].Visible);
    }

    public EditorWindowLayout CaptureLayout()
    {
        var result = new EditorWindowLayout { Windows = new EditorWindowPlacement[_panels.Count] };
        var index = 0;
        foreach (var entry in _panels)
        {
            var panel = entry.Value;
            result.Windows[index++] = new EditorWindowPlacement
            {
                Id = entry.Key,
                X = panel.Rect.anchoredPosition.x / _root.rect.width,
                Y = panel.Rect.anchoredPosition.y / _root.rect.height,
                Width = panel.Rect.sizeDelta.x,
                Height = panel.Rect.sizeDelta.y,
                Visible = panel.Visible,
            };
        }
        return result;
    }

    public void RestoreLayout(EditorWindowLayout? layout)
    {
        if (layout == null || layout.Version != 1 || layout.Windows == null)
            return;
        foreach (var saved in layout.Windows)
        {
            if (saved == null || saved.Id == null || !_panels.TryGetValue(saved.Id, out var panel))
                continue;
            ApplyPlacement(panel, EditorWindowPlacement.Fit(saved, _root.rect.width, _root.rect.height, panel.Minimum.x, panel.Minimum.y));
        }
        FitContents();
        ApplyVisibility();
    }

    private void ApplyPlacement(Panel panel, EditorWindowPlacement saved)
    {
        panel.Rect.sizeDelta = new Vector2(saved.Width, saved.Height);
        panel.Rect.anchoredPosition = new Vector2(saved.X * _root.rect.width, saved.Y * _root.rect.height);
        panel.Visible = saved.Visible;
    }

    private void FitPanel(string id, Panel panel)
    {
        var rect = panel.Rect;
        panel.Drag.BottomInset = panel.Resize.BottomInset = _capture ? 88 : 40;
        ApplyPlacement(
            panel,
            EditorWindowPlacement.Fit(
                new EditorWindowPlacement
                {
                    Id = id,
                    Width = rect.sizeDelta.x,
                    Height = rect.sizeDelta.y,
                    Visible = panel.Visible,
                    X = rect.anchoredPosition.x / _root.rect.width,
                    Y = rect.anchoredPosition.y / _root.rect.height,
                },
                _root.rect.width,
                _root.rect.height,
                panel.Minimum.x,
                panel.Minimum.y,
                panel.Drag.BottomInset
            )
        );
    }

    private void Place(string id, float x, float y, float width, float height)
    {
        var rect = (RectTransform)_controls[id];
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x + width / 2, -y - height / 2);
    }

    private void FitToolbar()
    {
        var width = _root.rect.width;
        Place("WorkspaceTitleBar", 0, 0, width, 40);
        Place("WorkspaceTitle", 10, 4, 180, 32);
        Place("Connection", 198, 4, width - 608, 32);
        _controls["Connection"].GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Wrap;
        var x = width - 396;
        foreach (
            var item in new[]
            {
                ("ContextToggle", "Session", 100),
                ("WindowsToggle", "Windows", 110),
                ("HelpToggle", "Help", 76),
                ("CloseEditor", "Close", 80),
            }
        )
        {
            Place(item.Item1, x, 7, item.Item3, 26);
            x += item.Item3 + 6;
            _controls[item.Item1].GetComponentInChildren<Text>(true).text = item.Item2;
        }
        Place("TransformToolbar", 0, 40, width, 40);
        x = 6;
        foreach (var item in new[] { ("Undo", 80), ("Redo", 80), ("Move", 80), ("Rotate", 84), ("Scale", 84), ("Snap", 108) })
        {
            Place(item.Item1, x, 3, item.Item2, 30);
            x += item.Item2 + 6;
        }
        Place("CameraSpeedLabel", x + 8, 3, 62, 30);
        Place("CameraSlower", x + 72, 3, 26, 30);
        Place("CameraSpeed", x + 102, 3, 56, 30);
        Place("CameraFaster", x + 162, 3, 26, 30);
        Place("EditorMapToolbar", width - 298, 0, 292, 36);
        Place("EditorWalk", 0, 3, 140, 30);
        Place("EditorReset", 146, 3, 140, 30);
        Place("StatusBar", 0, _root.rect.height - 24, width, 24);
        Place("Status", 8, 0, width * .55f, 24);
        Place("Request", width * .57f, 0, width * .43f - 12, 24);
        _controls["Request"].GetComponent<Text>().alignment = TextAnchor.MiddleRight;
        Place("ActionBar", 0, _root.rect.height - 80, width, 40);
        UiElements.Stretch((RectTransform)_controls["CaptureTask"]);
        Place("CaptureRequest", 8, 0, width - 350, 40);
        // Keep menus aligned with the right-hand toolbar controls.
        foreach (var id in _menus)
        {
            var rect = (RectTransform)_controls[id];
            Place(id, width - rect.sizeDelta.x - 8, 44, rect.sizeDelta.x, rect.sizeDelta.y);
        }
    }

    private void CachePropertyGeometry()
    {
        foreach (var id in new[] { "RecordInspector", "MapInspector", "SceneInspector" })
        foreach (Transform row in _controls[id])
        foreach (Transform child in row)
        {
            var rect = child as RectTransform;
            if (rect && !(child.GetComponent<RawImage>()) && rect!.anchorMin == rect.anchorMax)
                _propertyGeometry.Add((rect, rect.anchoredPosition, rect.sizeDelta, (RectTransform)row));
        }
    }

    private void UpdateDetails()
    {
        Visible("IdentityGroup", _identityAvailable && _recordDetails);
        Visible("DetailsGroup", _recordAvailable && _recordDetails);
        Visible("MapDetailsGroup", _mapDetails);
        _controls["RecordDetailsToggle"].GetComponentInChildren<Text>().text = _recordDetails ? "Details −" : "Details +";
        _controls["MapDetailsToggle"].GetComponentInChildren<Text>().text = _mapDetails ? "Details −" : "Details +";
    }

    private void FitContents()
    {
        if (_panels.Count == 0)
            return;
        var help = _controls["Help"].GetComponent<Text>();
        var helpWidth = _panels["Controls"].Rect.rect.width - 46;
        if (help.rectTransform.sizeDelta.x != helpWidth)
        {
            Place("Help", 4, 4, helpWidth, 210);
            var helpHeight = Mathf.Max(170, help.preferredHeight);
            Place("Help", 4, 4, helpWidth, helpHeight);
            _controls["ControlsScroll"].GetComponent<ScrollRect>().content.sizeDelta = new Vector2(0, helpHeight + 12);
        }
        var propertyWidth = _panels["Inspector"].Rect.rect.width;
        if (_propertyWidth != propertyWidth)
        {
            _propertyWidth = propertyWidth;
            UiElements.Stretch((RectTransform)_controls["PropertyScroll"], 8, 8, 38, 16);
            var ratio = (propertyWidth - 42) / 318;
            foreach (var entry in _propertyGeometry)
            {
                entry.Rect.sizeDelta = new Vector2(entry.Size.x * ratio, entry.Size.y);
                entry.Rect.anchoredPosition = new Vector2(entry.Position.x * ratio, entry.Position.y);
            }
        }
        var width = _panels["Library"].Rect.rect.width;
        var mask = 0;
        var count = 0;
        var bit = 1;
        foreach (Transform child in _controls["CreationTools"])
        {
            if (child.gameObject.activeSelf)
            {
                count++;
                mask |= bit;
            }
            bit <<= 1;
        }
        var scene = _controls["SceneTabs"].gameObject.activeSelf;
        var filters = _controls["SceneFilters"].gameObject.activeSelf;
        if (scene)
            mask |= 1 << 24;
        if (filters)
            mask |= 1 << 25;
        if (_contentWidth == width && _contentMask == mask)
            return;
        _contentWidth = width;
        _contentMask = mask;
        var inner = width - 16;
        Place("CategoryRail", 8, 36, inner, 60);
        for (var i = 0; i < _categories.Length; i++)
            Place(_categories[i], i % 3 * (inner + 4) / 3, i / 3 * 30, (inner - 8) / 3, 27);
        Place("Search", 8, 104, inner, 32);
        Place("SceneTabs", 8, 142, inner, 30);
        Place("SceneFilters", 8, 178, inner, 30);
        var col = 0;
        foreach (var id in new[] { "SceneCatalog", "SceneExisting", "SceneChanges", "SceneProps", "SceneLoot", "ScenePresets" })
            Place(id, col++ % 3 * (inner + 4) / 3, 0, (inner - 8) / 3, 28);
        var rows = (count + 2) / 3;
        var height = rows * 34;
        var actions = (RectTransform)_controls["CreationTools"];
        actions.anchorMin = new Vector2(0, 0);
        actions.anchorMax = new Vector2(1, 0);
        actions.pivot = new Vector2(.5f, 0);
        actions.sizeDelta = new Vector2(-16, height);
        actions.anchoredPosition = new Vector2(0, 68);
        var index = 0;
        foreach (Transform child in actions)
        {
            if (!child.gameObject.activeSelf)
                continue;
            var rect = (RectTransform)child;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            var buttonWidth = (inner - 8) / 3;
            rect.sizeDelta = new Vector2(buttonWidth, 30);
            rect.anchoredPosition = new Vector2(index % 3 * (buttonWidth + 4) + buttonWidth / 2, -(index / 3 * 34 + 15));
            index++;
        }
        UiElements.Stretch(
            (RectTransform)_controls["LibraryScroll"],
            8,
            8,
            filters ? 218
                : scene ? 182
                : 146,
            78 + height
        );
        var paging = (RectTransform)_controls["Paging"];
        paging.anchorMin = Vector2.zero;
        paging.anchorMax = new Vector2(1, 0);
        paging.pivot = new Vector2(.5f, 0);
        paging.sizeDelta = new Vector2(0, 38);
        paging.anchoredPosition = new Vector2(0, 8);
        Place("Previous", 8, 2, (inner - 4) / 2, 30);
        Place("Next", 12 + (inner - 4) / 2, 2, (inner - 4) / 2, 30);
        var counter = (RectTransform)_controls["LibraryCount"];
        counter.anchorMin = counter.anchorMax = new Vector2(.5f, 0);
        counter.anchoredPosition = new Vector2(0, 56);
        counter.sizeDelta = new Vector2(inner, 22);
        counter.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
    }
}
