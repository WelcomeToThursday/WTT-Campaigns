using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    private readonly VisualElement _menuShield = new();
    private string _openMenu = "";
    private int _menuDismissFrame = -1;
    internal bool MenuDismissedThisFrame => _menuDismissFrame == Time.frameCount;

    private void SetupMenus()
    {
        _menuShield.style.position = Position.Absolute;
        _menuShield.style.left = _menuShield.style.right = _menuShield.style.top = _menuShield.style.bottom = 0;
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
            menu.style.paddingTop = menu.style.paddingBottom = 4;
            menu.style.paddingLeft = menu.style.paddingRight = 0;
            menu.style.marginLeft = menu.style.marginRight = menu.style.marginTop = menu.style.marginBottom = 0;
            menu.style.width = 300;
            menu.style.right = StyleKeyword.Auto;
            menu.RegisterCallback<KeyDownEvent>(MenuKey, TrickleDown.TrickleDown);
            var sizeActions = menu.Q("UiSizeActions");
            if (sizeActions != null)
                sizeActions.style.flexDirection = FlexDirection.Column;
            foreach (var button in menu.Query<Button>().ToList())
            {
                button.style.alignSelf = Align.Stretch;
                button.style.width = StyleKeyword.Auto;
                button.style.maxWidth = StyleKeyword.None;
                button.style.flexGrow = 0;
                button.style.height = button.style.minHeight = 28;
                button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0;
                button.style.borderLeftWidth =
                    button.style.borderRightWidth =
                    button.style.borderTopWidth =
                    button.style.borderBottomWidth =
                        0;
                button.style.paddingLeft = button.style.paddingRight = 12;
                button.style.paddingTop = button.style.paddingBottom = 0;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.whiteSpace = WhiteSpace.NoWrap;
                button.style.backgroundColor = Color.clear;
                button.RegisterCallback<PointerEnterEvent>(_ => button.Focus());
                button.RegisterCallback<FocusInEvent>(_ => button.style.backgroundColor = new Color(.30f, .28f, .20f));
                button.RegisterCallback<FocusOutEvent>(_ => button.style.backgroundColor = Color.clear);
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
