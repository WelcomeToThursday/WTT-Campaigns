using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Controls;

// Window lifecycle is independent of authoring data, selection and tool actions.
public sealed class RaidEditorWindows : MonoBehaviour
{
    private sealed class Panel
    {
        public RectTransform Rect = null!;
        public Transform Dock = null!;
        public Vector2 Position;
        public EditorWindowDrag Drag = null!;
        public Button Toggle = null!;
        public bool Floating;
        public GameObject Placeholder = null!;
    }

    private readonly List<Panel> _panels = new List<Panel>();
    private RectTransform _root = null!,
        _workspace = null!;
    private EditorWindowDrag _workspaceDrag = null!;
    private Vector2 _home,
        _size;
    private Transform _shield = null!;

    public void Initialize()
    {
        Canvas.ForceUpdateCanvases();
        _root = (RectTransform)transform;
        _workspace = (RectTransform)transform.Find("Workspace");
        _shield = transform.Find("ConflictShield");
        _home = _workspace.anchoredPosition;
        _workspaceDrag = Drag(_workspace, _workspace.Find("WorkspaceTitleBar"));
        var dock = _workspace.Find("DockArea");
        foreach (Transform child in dock)
        {
            var bar = child.Find(child.name + "TitleBar");
            if (!bar)
                continue;
            var panel = new Panel
            {
                Rect = (RectTransform)child,
                Dock = dock,
                Position = ((RectTransform)child).anchoredPosition,
                Drag = Drag((RectTransform)child, bar),
                Toggle = bar.Find(child.name + "Popout").GetComponent<Button>(),
                Placeholder = dock.Find(child.name + "Placeholder").gameObject,
            };
            panel.Drag.Movable = false;
            panel.Toggle.onClick.AddListener(() => Toggle(panel));
            panel.Placeholder.GetComponentInChildren<Button>(true).onClick.AddListener(() => Dock(panel));
            _panels.Add(panel);
        }
        var title = _workspace.Find("WorkspaceTitleBar");
        title.Find("ResetLayout").GetComponent<Button>().onClick.AddListener(ResetLayout);
        var help = transform.Find("Controls").gameObject;
        title
            .Find("HelpToggle")
            .GetComponent<Button>()
            .onClick.AddListener(() =>
            {
                help.SetActive(!help.activeSelf);
                help.transform.SetAsLastSibling();
                KeepModalOnTop();
            });
        ResetLayout();
    }

    private EditorWindowDrag Drag(RectTransform window, Transform handle)
    {
        var drag = handle.gameObject.AddComponent<EditorWindowDrag>();
        drag.Window = window;
        drag.Boundary = _root;
        return drag;
    }

    private void Toggle(Panel panel)
    {
        if (panel.Floating)
            Dock(panel);
        else
        {
            panel.Rect.SetParent(_root, true);
            panel.Rect.anchorMin = panel.Rect.anchorMax = new Vector2(.5f, .5f);
            panel.Rect.anchoredPosition = new Vector2(_workspace.anchoredPosition.x + 620, 0);
            panel.Rect.SetAsLastSibling();
            panel.Floating = panel.Drag.Movable = true;
            panel.Placeholder.SetActive(true);
            panel.Toggle.GetComponentInChildren<Text>().text = "Dock";
            panel.Drag.Clamp();
        }
        KeepModalOnTop();
    }

    private static void Dock(Panel panel)
    {
        panel.Rect.SetParent(panel.Dock, false);
        panel.Rect.anchorMin = panel.Rect.anchorMax = new Vector2(.5f, .5f);
        panel.Rect.anchoredPosition = panel.Position;
        panel.Floating = panel.Drag.Movable = false;
        panel.Placeholder.SetActive(false);
        panel.Toggle.GetComponentInChildren<Text>().text = "Pop out";
    }

    public void ResetLayout()
    {
        foreach (var panel in _panels)
            Dock(panel);
        _workspace.anchoredPosition = _home;
        _workspaceDrag.Clamp();
        transform.Find("Controls").gameObject.SetActive(false);
    }

    public void KeepModalOnTop()
    {
        if (_shield.gameObject.activeSelf)
            _shield.SetAsLastSibling();
    }

    private void LateUpdate()
    {
        if (_root.rect.size != _size)
        {
            _size = _root.rect.size;
            _workspaceDrag.Clamp();
            foreach (var panel in _panels)
                panel.Drag.Clamp();
        }
        KeepModalOnTop();
    }
}
