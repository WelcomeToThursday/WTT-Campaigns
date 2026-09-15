using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private ScrollView _paged = null!;
    private ListView _tree = null!;
    private readonly List<EditorTreeNode> _treeRows = new();
    private readonly Dictionary<string, HashSet<string>> _expanded = new();
    private EditorTreeModel? _treeModel;
    private Func<ISet<string>, EditorTreeModel>? _treeBuild;
    private Action<string>? _selectTree;
    private string _treeContext = "",
        _treeSearch = "",
        _treeSelection = "";
    private long _treeRevision = long.MinValue;
    private bool _treeSync;
    internal int TreeRecordCount => _treeModel?.SelectableCount ?? 0;
    internal int TreeVisibleCount => _treeRows.Count;

    private void BuildBrowser()
    {
        var parent = Element("LibraryScroll");
        _paged = new ScrollView();
        EditorScrollStyle.Apply(_paged);
        _paged.style.flexGrow = 1;
        _paged.style.minHeight = 0;
        _paged.style.flexBasis = 0;
        parent.Add(_paged);
        for (var i = 0; i < 10; i++)
        {
            var button = new Button();
            button.AddToClassList("editor-browser-row");
            var control = new EditorButton(button);
            Register("Row" + i, control);
            _rows.Add(control);
            _paged.Add(button);
            var image = new Image();
            image.AddToClassList("editor-browser-icon");
            Register("SceneIcon" + i, new EditorImage(image));
            button.Add(image);
            var status = new Label();
            status.AddToClassList("editor-browser-status");
            Register("SceneIconStatus" + i, new EditorLabel(status));
            button.Add(status);
            Visible("SceneIcon" + i, false);
            Visible("SceneIconStatus" + i, false);
        }
        _tree = new ListView
        {
            fixedItemHeight = 30,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            selectionType = SelectionType.Single,
            itemsSource = _treeRows,
            makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("editor-tree-row");
                var fold = new Button();
                fold.style.width = 24;
                fold.style.flexShrink = 0;
                row.Add(fold);
                var label = new Label { enableRichText = false };
                row.Add(label);
                fold.clicked += () =>
                {
                    if (row.userData is EditorTreeNode node && node.HasChildren)
                    {
                        var expanded = _expanded[_treeContext];
                        if (!expanded.Remove(node.Key))
                            expanded.Add(node.Key);
                        RebuildTree(false);
                    }
                };
                row.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (row.userData is EditorTreeNode node)
                        Windows.ShowTooltip("", node.Path, row);
                });
                row.RegisterCallback<PointerLeaveEvent>(_ => Windows.HideTooltip());
                return row;
            },
            bindItem = (row, index) =>
            {
                var node = _treeRows[index];
                row.userData = node;
                row.style.paddingLeft = node.Depth * 14;
                row.Q<Label>().text = node.Label;
                var fold = row.Q<Button>();
                fold.text = node.HasChildren ? (_treeSearch.Length > 0 || _expanded[_treeContext].Contains(node.Key) ? "−" : "+") : "";
                fold.SetEnabled(node.HasChildren);
            },
        };
        _tree.style.flexGrow = 1;
        _tree.style.minHeight = 0;
        _tree.style.flexBasis = 0;
        EditorScrollStyle.Apply(_tree.Q<ScrollView>());
        parent.Add(_tree);
        _tree.style.display = DisplayStyle.None;
        _tree.selectionChanged += selection =>
        {
            if (_treeSync)
                return;
            foreach (var item in selection)
            {
                if (item is EditorTreeNode { Selectable: true } node)
                    _selectTree?.Invoke(node.Id);
                break;
            }
        };
    }

    internal void BindTreeSelection(Action<string> action) => _selectTree = action;

    internal void RefreshTree(string contextId, long revision, string search, string selection, Func<ISet<string>, EditorTreeModel> build)
    {
        _paged.style.display = DisplayStyle.None;
        _tree.style.display = DisplayStyle.Flex;
        if (_treeContext == contextId && _treeRevision == revision && _treeSearch == search && _treeSelection == selection)
            return;
        var reveal = _treeContext != contextId || _treeSelection != selection;
        _treeContext = contextId;
        _treeRevision = revision;
        _treeSearch = search;
        _treeSelection = selection;
        _treeBuild = build;
        if (!_expanded.ContainsKey(contextId))
            _expanded[contextId] = EditorTreeModel.DefaultExpanded(build(new HashSet<string>()));
        RebuildTree(reveal);
    }

    private void RebuildTree(bool reveal)
    {
        if (_treeBuild == null)
            return;
        var expanded = _expanded[_treeContext];
        _treeModel = _treeBuild(expanded);
        if (reveal && EditorTreeModel.ExpandForSelection(_treeModel, _treeSelection, expanded))
            _treeModel = _treeBuild(expanded);
        _treeSync = true;
        try
        {
            _treeRows.Clear();
            _treeRows.AddRange(_treeModel.Visible);
            _tree.RefreshItems();
            var index = _treeRows.FindIndex(n => n.Selectable && n.Id == _treeSelection);
            _tree.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
            if (reveal && index >= 0)
                _tree.ScrollToItem(index);
        }
        finally
        {
            _treeSync = false;
        }
    }

    internal void HideTree()
    {
        _tree.style.display = DisplayStyle.None;
        _paged.style.display = DisplayStyle.Flex;
    }
}
