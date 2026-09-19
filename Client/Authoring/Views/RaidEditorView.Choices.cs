using UnityEngine;
using UnityEngine.UIElements;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private VisualElement? _choiceAnchor;
    private Action? _positionChoice;
    private int _dropdownDismissFrame = -1;

    internal bool DismissDropdowns()
    {
        var viewportMenu = DismissViewportMenu();
        if (_choicePopup == null)
            return viewportMenu;
        _choicePopup.RemoveFromHierarchy();
        _choicePopup = null;
        _positionChoice = null;
        Windows.HideTooltip();
        _dropdownDismissFrame = Time.frameCount;
        if (_choiceAnchor?.panel != null && _choiceAnchor.enabledInHierarchy)
            _choiceAnchor.Focus();
        _choiceAnchor = null;
        return true;
    }

    private void OpenChoice(EditorChoice choice)
    {
        if (!choice.interactable)
            return;
        var labels = choice.options.AsValueEnumerable().Select(o => o.text).ToArray();
        ShowChoices(
            choice.Element,
            labels,
            choice.value,
            _ => true,
            index =>
            {
                // A live refresh may replace options while the menu is open.
                if (index >= choice.options.Count || choice.options[index].text != labels[index])
                    return;
                choice.SetValueWithoutNotify(index);
                choice.onValueChanged.Invoke(index);
            }
        );
    }

    private void ShowChoices(VisualElement anchor, string[] labels, int selected, Func<int, bool> enabled, Action<int> choose)
    {
        DismissDropdowns();
        Windows.DismissMenus();
        Windows.HideTooltip();
        _choiceAnchor = anchor;
        var shield = Document.Clone<VisualElement>("ChoicePopup");
        _choicePopup = shield;
        Document.Content.Add(shield);
        var panel = shield.Q<VisualElement>("ChoicePanel");
        var search = shield.Q<TextField>("ChoiceSearch");
        var empty = shield.Q<Label>("ChoiceEmpty");
        var list = shield.Q<ListView>("Choices");
        var matches = EditorInteractionPolicy.Matches(labels, "");
        var searching = labels.Length > 10;
        search.style.display = searching ? DisplayStyle.Flex : DisplayStyle.None;
        list.fixedItemHeight = 30;
        list.selectionType = SelectionType.Single;
        list.makeItem = () =>
        {
            var button = Document.Clone<Button>("ChoiceOption");
            button.enableRichText = false;
            button.clicked += () => Select((int)button.userData);
            button.RegisterCallback<PointerEnterEvent>(_ => Windows.ShowTooltip("", button.tooltip, button));
            button.RegisterCallback<PointerLeaveEvent>(_ => Windows.HideTooltip());
            return button;
        };
        list.bindItem = (element, row) =>
        {
            var index = matches[row];
            var button = (Button)element;
            button.userData = index;
            button.text = labels[index];
            button.tooltip = labels[index];
            button.SetEnabled(enabled(index));
            button.EnableInClassList("editor-selected", index == selected);
        };
        list.itemsSource = matches;
        EditorScrollStyle.Apply(list.Q<ScrollView>());
        void Select(int index)
        {
            if (!enabled(index))
                return;
            DismissDropdowns();
            choose(index);
        }
        void Filter()
        {
            matches = EditorInteractionPolicy.Matches(labels, search.value ?? "");
            list.itemsSource = matches;
            list.Rebuild();
            var row = matches.IndexOf(selected);
            if (row < 0 && matches.Count > 0)
                row = 0;
            list.SetSelectionWithoutNotify(row < 0 ? Array.Empty<int>() : new[] { row });
            empty.style.display = matches.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            list.style.display = matches.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _positionChoice?.Invoke();
            list.schedule.Execute(() =>
            {
                if (list.panel != null && row >= 0)
                    list.ScrollToItem(row);
            });
        }
        _positionChoice = () =>
        {
            var visible = anchor.panel != null;
            for (var parent = anchor; parent != null; parent = parent.parent)
                visible &= parent.resolvedStyle.display != DisplayStyle.None;
            if (!visible)
            {
                DismissDropdowns();
                return;
            }
            var bounds = anchor.worldBound;
            var min = Document.Content.WorldToLocal(bounds.min);
            var max = Document.Content.WorldToLocal(bounds.max);
            var desired = Math.Min(300, Math.Max(1, matches.Count) * 30) + (searching ? 38 : 0) + 12;
            var placement = EditorInteractionPolicy.Popup(min.x, min.y, max.y, bounds.width, desired, Document.Width, Document.Height);
            panel.style.left = placement.X;
            panel.style.top = placement.Y;
            panel.style.width = placement.Width;
            panel.style.height = placement.Height;
        };
        search.RegisterValueChangedCallback(_ => Filter());
        shield.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == shield)
                DismissDropdowns();
            evt.StopPropagation();
        });
        shield.RegisterCallback<KeyDownEvent>(
            evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                    DismissDropdowns();
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    if (list.selectedIndex >= 0 && list.selectedIndex < matches.Count)
                        Select(matches[list.selectedIndex]);
                }
                else if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow)
                {
                    var direction = evt.keyCode == KeyCode.DownArrow ? 1 : -1;
                    var row = list.selectedIndex;
                    for (var n = 0; n < matches.Count; n++)
                    {
                        row = (row + direction + matches.Count) % matches.Count;
                        if (enabled(matches[row]))
                        {
                            list.SetSelectionWithoutNotify(new[] { row });
                            list.ScrollToItem(row);
                            break;
                        }
                    }
                }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    if (searching && !(evt.target is TextField) && !search.Contains(evt.target as VisualElement))
                        search.Focus();
                    else
                        list.Focus();
                }
                else
                    return;
                evt.StopPropagation();
                evt.PreventDefault();
            },
            TrickleDown.TrickleDown
        );
        Filter();
        if (searching)
            search.Focus();
        else
            list.Focus();
    }
}
