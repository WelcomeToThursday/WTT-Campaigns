using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private sealed class BrowserState
    {
        internal readonly List<EditorButton> Rows = new();
        internal ScrollView Paged = null!;
        internal ListView Tree = null!;
        internal readonly List<EditorTreeNode> TreeRows = new();
        internal readonly Dictionary<string, HashSet<string>> Expanded = new();
        internal EditorTreeModel? Model;
        internal Func<ISet<string>, EditorTreeModel>? Build;
        internal string Context = "",
            Search = "",
            Selection = "";
        internal long Revision = long.MinValue;
        internal bool Sync,
            PointerHeld;
        internal bool Grid = true;
        internal int Presentation = -1;
    }

    private readonly Dictionary<string, BrowserState> _browsers = new();
    private BrowserState Browser => _browsers[ToolContext];
    private ScrollView _paged => Browser.Paged;
    private ListView _tree => Browser.Tree;
    private List<EditorTreeNode> _treeRows => Browser.TreeRows;
    private Dictionary<string, HashSet<string>> _expanded => Browser.Expanded;
    private EditorTreeModel? _treeModel
    {
        get => Browser.Model;
        set => Browser.Model = value;
    }
    private Func<ISet<string>, EditorTreeModel>? _treeBuild
    {
        get => Browser.Build;
        set => Browser.Build = value;
    }
    private string _treeContext
    {
        get => Browser.Context;
        set => Browser.Context = value;
    }
    private string _treeSearch
    {
        get => Browser.Search;
        set => Browser.Search = value;
    }
    private string _treeSelection
    {
        get => Browser.Selection;
        set => Browser.Selection = value;
    }
    private long _treeRevision
    {
        get => Browser.Revision;
        set => Browser.Revision = value;
    }
    private bool _treeSync
    {
        get => Browser.Sync;
        set => Browser.Sync = value;
    }
    private Action<string>? _selectTree;
    internal int TreeRecordCount => _treeModel?.SelectableCount ?? 0;
    internal int TreeVisibleCount => _treeRows.Count;

    private void BuildBrowser()
    {
        var owner = ToolContext;
        var state = Browser;
        var parent = Element("LibraryScroll");
        Browser.Paged = new ScrollView();
        EditorScrollStyle.Apply(_paged);
        _paged.style.flexGrow = 1;
        _paged.style.minHeight = 0;
        _paged.style.flexBasis = 0;
        _paged.contentViewport.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            if (state.Presentation == 2)
                state.Paged.contentContainer.style.width = evt.newRect.width;
        });
        parent.Add(_paged);
        for (var i = 0; i < (owner == "Scene" ? CatalogGridLayout.MaximumItems : 10); i++)
        {
            var button = new Button();
            button.AddToClassList("editor-browser-row");
            EditorControlLayout.ListRow(button);
            button.style.minHeight = 28;
            button.style.marginTop = button.style.marginBottom = 1;
            var control = new EditorButton(button);
            button.style.display = DisplayStyle.None;
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
        Browser.Tree = new ListView
        {
            fixedItemHeight = EditorControlLayout.TreeRowHeight,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            selectionType = SelectionType.Single,
            itemsSource = _treeRows,
            makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("editor-tree-row");
                // Use the native theme arrow: the recovered EFT font has no triangle glyphs.
                var fold = new Foldout { text = "", focusable = false };
                row.Add(fold);
                var label = new Label { name = "tree-label", enableRichText = false };
                EditorControlLayout.TreeRow(row, fold, label);
                row.Add(label);
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (row.userData is EditorTreeNode node && node.HasChildren)
                    {
                        if (!Activate(owner))
                            return;
                        var expanded = state.Expanded[state.Context];
                        if (evt.newValue)
                            expanded.Add(node.Key);
                        else
                            expanded.Remove(node.Key);
                        // Finish the toggle event before rebinding recycled list rows.
                        state.Tree.schedule.Execute(() =>
                        {
                            if (Activate(owner))
                                RebuildTree(false);
                        });
                    }
                });
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
                var node = state.TreeRows[index];
                row.userData = node;
                row.style.paddingLeft = 4 + node.Depth * 14;
                row.Q<Label>("tree-label").text = node.Label;
                var fold = row.Q<Foldout>();
                fold.SetValueWithoutNotify(
                    node.HasChildren && (state.Search.Length > 0 || state.Expanded[state.Context].Contains(node.Key))
                );
                fold.contentContainer.style.display = DisplayStyle.None;
                fold.SetEnabled(node.HasChildren);
                fold.style.visibility = node.HasChildren ? Visibility.Visible : Visibility.Hidden;
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
            if (state.Sync)
                return;
            foreach (var item in selection)
            {
                if (item is EditorTreeNode { Selectable: true } node)
                {
                    var id = node.Id;
                    if (Activate(owner))
                        _selectTree?.Invoke(id);
                }
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
