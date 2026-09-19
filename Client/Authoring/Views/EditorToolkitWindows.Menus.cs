using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    private VisualElement _menuShield = null!;
    private string _openMenu = "";
    private int _menuDismissFrame = -1;
    internal bool MenuDismissedThisFrame => _menuDismissFrame == Time.frameCount;

    private void SetupMenus()
    {
        _menuShield = _view.Document.Clone<VisualElement>("MenuShield");
        _menuShield.style.display = DisplayStyle.None;
        _view.Element("Workspace").Add(_menuShield);
        _menuShield.RegisterCallback<PointerDownEvent>(evt =>
        {
            DismissMenus();
            evt.StopPropagation();
        });
        foreach (var id in new[] { "WindowsMenu", "ContextMenu" })
        {
            var menu = _view.Element(id);
            menu.RegisterCallback<KeyDownEvent>(MenuKey, TrickleDown.TrickleDown);
            foreach (var button in menu.Query<Button>().ToList())
            {
                button.RegisterCallback<PointerEnterEvent>(_ => button.Focus());
                if (button.name != "UiSizeSmaller" && button.name != "UiSizeLarger")
                    button.clicked += () => DismissMenus();
            }
        }
        foreach (var (button, menu) in new[] { ("WindowsToggle", "WindowsMenu"), ("ContextToggle", "ContextMenu") })
            _view
                .Element(button)
                .RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (_openMenu.Length > 0 && _openMenu != menu)
                        ToggleMenu(menu);
                });
    }

    private void OpenMenu(string id)
    {
        _view.DismissDropdowns();
        _view.ReleaseFocus();
        _openMenu = id;
        _menuShield.style.display = DisplayStyle.Flex;
        _menuShield.BringToFront();
        _view.Element("WorkspaceTitleBar").BringToFront();
        _view.Element(id).BringToFront();
        PositionMenu();
        var buttons = MenuButtons();
        if (buttons.Count > 0)
            buttons[0].Focus();
    }

    private List<Button> MenuButtons() =>
        _view
            .Element(_openMenu)
            .Query<Button>()
            .ToList()
            .FindAll(b => b.enabledInHierarchy && b.resolvedStyle.display != DisplayStyle.None);

    private void PositionMenu()
    {
        var menu = _view.Element(_openMenu);
        var anchor = _view.Element(_openMenu == "WindowsMenu" ? "WindowsToggle" : "ContextToggle").worldBound;
        var local = menu.parent.WorldToLocal(new Vector2(anchor.xMax, anchor.yMax));
        var width = Math.Min(300, _view.Document.Width - 16);
        menu.style.width = width;
        menu.style.left = Mathf.Clamp(local.x - width, 8, Math.Max(8, _view.Document.Width - width - 8));
        menu.style.top = local.y + 2;
    }

    private void MenuKey(KeyDownEvent evt)
    {
        if (_openMenu.Length == 0)
            return;
        var buttons = MenuButtons();
        var focused = _view.Document.Root.panel.focusController.focusedElement as Button;
        var index = buttons.IndexOf(focused!);
        if (evt.keyCode == KeyCode.Escape)
            DismissMenus();
        else if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow)
        {
            if (buttons.Count > 0)
                buttons[(index + (evt.keyCode == KeyCode.DownArrow ? 1 : buttons.Count - 1) + buttons.Count) % buttons.Count].Focus();
        }
        else if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && focused != null && focused.enabledInHierarchy)
        {
            var name = focused.name;
            if (name != "UiSizeSmaller" && name != "UiSizeLarger")
                DismissMenus();
            _view.Get<EditorButton>(name).onClick.Invoke();
        }
        else
            return;
        evt.StopPropagation();
        evt.PreventDefault();
    }
}
