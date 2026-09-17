using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    internal Action? CatalogCapacityChanged;
    internal int RowCapacity => _rows.Count;
    internal int CatalogPageSize =>
        Browser.Grid
            ? CatalogGridLayout.Capacity(_paged.contentViewport.resolvedStyle.width, _paged.contentViewport.resolvedStyle.height)
            : 10;
    private int _lastCatalogCapacity;

    internal void PollCatalogCapacity()
    {
        if (ToolContext != "Scene" || RowPressed || Windows.Interacting)
            return;
        var size = CatalogPageSize;
        if (_lastCatalogCapacity == size)
            return;
        _lastCatalogCapacity = size;
        CatalogCapacityChanged?.Invoke();
    }

    private void SetCatalogGrid(string tool, bool grid)
    {
        if (!Activate(tool))
            return;
        Browser.Grid = grid;
        ApplyCatalogPresentation(Browser.Presentation > 0);
        PollCatalogCapacity();
    }

    private void ApplyCatalogPresentation(bool thumbnails)
    {
        var grid = thumbnails && Browser.Grid;
        var presentation =
            grid ? 2
            : thumbnails ? 1
            : 0;
        if (Browser.Presentation == presentation)
            return;
        Browser.Presentation = presentation;
        // Reserve the scrollbar gutter so adding/removing a grid row cannot
        // repeatedly change the measured column count and fetch a different page.
        _paged.verticalScrollerVisibility = grid ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Auto;
        Visible("CatalogViews", thumbnails);
        Highlight("CatalogGrid", grid);
        Highlight("CatalogList", !grid);
        var content = _paged.contentContainer;
        content.style.flexDirection = grid ? FlexDirection.Row : FlexDirection.Column;
        content.style.flexWrap = grid ? Wrap.Wrap : Wrap.NoWrap;
        content.style.alignContent = Align.FlexStart;
        content.style.alignItems = grid ? Align.FlexStart : Align.Stretch;
        var width = _paged.contentViewport.resolvedStyle.width;
        content.style.width = grid && float.IsFinite(width) && width > 0 ? new StyleLength(width) : new StyleLength(StyleKeyword.Null);
        _paged.scrollOffset = Vector2.zero;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i].Element;
            var tileWidth = CatalogGridLayout.TileWidth - 2 * CatalogGridLayout.TileMargin;
            var tileHeight = CatalogGridLayout.TileHeight - 2 * CatalogGridLayout.TileMargin;
            row.style.width = grid ? new StyleLength(tileWidth) : new StyleLength(StyleKeyword.Auto);
            row.style.height = grid ? new StyleLength(tileHeight) : new StyleLength(StyleKeyword.Auto);
            row.style.minHeight =
                grid ? tileHeight
                : thumbnails ? 50
                : 28;
            row.style.maxHeight = grid ? new StyleLength(tileHeight) : new StyleLength(StyleKeyword.None);
            row.style.flexGrow = row.style.flexShrink = 0;
            row.style.alignSelf = grid ? Align.FlexStart : Align.Stretch;
            row.style.marginLeft = row.style.marginRight = grid ? CatalogGridLayout.TileMargin : 0;
            row.style.marginTop = row.style.marginBottom = grid ? CatalogGridLayout.TileMargin : 1;
            row.style.paddingLeft =
                grid ? 6
                : thumbnails ? 54
                : 8;
            row.style.paddingRight = 6;
            row.style.paddingTop = grid ? CatalogGridLayout.ThumbnailHeight + 10 : 4;
            row.style.paddingBottom = 4;
            row.style.unityTextAlign = grid ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
            row.style.fontSize = grid ? 13 : 15;
            row.style.whiteSpace = WhiteSpace.Normal;
            row.style.overflow = Overflow.Hidden;
            var image = Element("SceneIcon" + i);
            image.style.left = grid ? 8 : 4;
            image.style.top = grid ? 6 : 4;
            image.style.width = grid ? tileWidth - 16 : 40;
            image.style.height = grid ? CatalogGridLayout.ThumbnailHeight : 40;
            image.pickingMode = PickingMode.Ignore;
            var status = Element("SceneIconStatus" + i);
            status.style.left = grid ? 8 : 4;
            status.style.top = grid ? new StyleLength(28) : new StyleLength(StyleKeyword.Auto);
            status.style.bottom = grid ? new StyleLength(StyleKeyword.Auto) : new StyleLength(2);
            status.pickingMode = PickingMode.Ignore;
        }
    }
}
