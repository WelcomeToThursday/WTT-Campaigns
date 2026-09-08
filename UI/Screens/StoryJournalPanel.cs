using System;
using System.Collections.Generic;
using System.Linq;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed class StoryJournalPanel
{
    private readonly UiElements _ui;
    private readonly RectTransform _root;
    private readonly Func<string, Sprite?> _art;
    private readonly Action<string, string> _read;
    private readonly Action<string> _link;
    private StoryChapterView[] _chapters = Array.Empty<StoryChapterView>();
    private string _selected = "";
    private bool _history;
    private bool _completed;
    private Vector2 _size;
    private readonly List<(string Kind, string Id, RectTransform Row, RectTransform Viewport)> _unread = new();
    private readonly List<(ScrollRect Scroll, float Position)> _restoreScroll = new();
    private ScrollRect? _notesScroll;
    private ScrollRect? _chaptersScroll;
    private ScrollRect? _objectivesScroll;
    private ScrollRect? _linksScroll;
    private string _renderedChapter = "";
    private bool _renderedHistory;
    private bool _layoutPending;

    public StoryJournalPanel(RectTransform root, Font font, Func<string, Sprite?> art, Action<string, string> read, Action<string> link)
    {
        _root = root;
        _ui = new UiElements(font);
        _art = art;
        _read = read;
        _link = link;
    }

    public void SetState(StoryChapterView[] chapters)
    {
        _chapters = chapters;
        if (!chapters.Any(c => c.Id == _selected))
        {
            _selected = chapters.FirstOrDefault()?.Id ?? "";
        }
        Render();
    }

    public void Fit()
    {
        if (_size != _root.rect.size)
        {
            Render();
            return;
        }
        if (_layoutPending)
        {
            foreach (var entry in _restoreScroll)
            {
                entry.Scroll.verticalNormalizedPosition = entry.Position;
            }
            _restoreScroll.Clear();
            _layoutPending = false;
            return;
        }
        if (!_root.gameObject.activeInHierarchy)
        {
            return;
        }
        StoryJournalStyle.FitScrollbar(_chaptersScroll);
        StoryJournalStyle.FitScrollbar(_notesScroll);
        StoryJournalStyle.FitScrollbar(_objectivesScroll);
        StoryJournalStyle.FitScrollbar(_linksScroll);
        foreach (var entry in _unread.ToArray())
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(entry.Viewport, entry.Row);
            var viewport = entry.Viewport.rect;
            var visibleHeight = Math.Min(bounds.max.y, viewport.yMax) - Math.Max(bounds.min.y, viewport.yMin);
            // Compact native rows can be shorter than 24px; allow subpixel
            // layout rounding when the entire row is visible.
            if (visibleHeight > 0 && visibleHeight + .01f >= Math.Min(24, bounds.size.y))
            {
                _unread.Remove(entry);
                _read(entry.Kind, entry.Id);
            }
        }
    }

    private void Render()
    {
        _size = _root.rect.size;
        var sameChapter = _selected == _renderedChapter;
        var notePosition = sameChapter && _history == _renderedHistory && _notesScroll ? _notesScroll!.verticalNormalizedPosition : 1;
        var objectivePosition = sameChapter && _objectivesScroll ? _objectivesScroll!.verticalNormalizedPosition : 1;
        var linkPosition = sameChapter && _linksScroll ? _linksScroll!.verticalNormalizedPosition : 1;
        _renderedChapter = _selected;
        _renderedHistory = _history;
        _unread.Clear();
        _restoreScroll.Clear();
        _chaptersScroll = _notesScroll = _objectivesScroll = _linksScroll = null;
        _layoutPending = true;
        for (var i = _root.childCount - 1; i >= 0; i--)
        {
            var child = _root.GetChild(i);
            child.gameObject.SetActive(false);
            UiElements.Destroy(child.gameObject);
        }
        UiElements.Fill(_root, Color.clear, true);
        if (_size.x <= 0 || _size.y <= 0)
            return;
        var width = _size.x;
        var height = _size.y;
        var selected = _chapters.FirstOrDefault(c => c.Id == _selected);
        if (selected == null)
        {
            var empty = _ui.Label(_root, "Empty story", "No chapters discovered yet.", 23, width - 48, 80);
            empty.alignment = TextAnchor.MiddleCenter;
            empty.color = new Color32(197, 195, 178, 255);
            return;
        }

        const float railWidth = 122;
        const float headerHeight = 150;
        var bodyWidth = width - railWidth;
        var center = railWidth / 2;
        RenderChapters(railWidth, height, width);
        var body = UiElements.Rect("Chapter content background", _root, bodyWidth, height - headerHeight, center, -headerHeight / 2);
        StoryJournalStyle.Artwork(body, "journal-content");
        RenderHeader(selected, bodyWidth, headerHeight, center, height);

        var lastNote = selected.Notes.LastOrDefault();
        var noteHeight =
            selected.Notes.Length == 0 ? 0
            : _history ? Math.Min(height * .38f, 360)
            : Mathf.Clamp(Measure(lastNote!.Text, 16, bodyWidth - 135, FontStyle.Italic) + 65, 90, height * .28f);
        if (noteHeight > 0)
        {
            var notesY = height / 2 - headerHeight - noteHeight / 2;
            var notes = StoryJournalStyle.Scroll(
                _ui,
                _root,
                "Journal history",
                bodyWidth,
                noteHeight,
                center,
                notesY,
                new RectOffset(59, 60, 40, 25)
            );
            notes.content.GetComponent<VerticalLayoutGroup>().spacing = 40;
            _notesScroll = notes;
            _restoreScroll.Add((notes, notePosition));
            foreach (var note in (_history ? selected.Notes : selected.Notes.TakeLast(1)))
            {
                var label = _ui.Label(notes.content, "Journal entry", note.Text, 16, bodyWidth - 135, 24);
                label.fontStyle = FontStyle.Italic;
                label.color = note == lastNote ? StoryJournalStyle.Text : new Color32(168, 167, 153, 255);
                label.gameObject.AddComponent<LayoutElement>().preferredHeight = Measure(note.Text, 16, bodyWidth - 135, FontStyle.Italic);
                if (note.Unread)
                {
                    StoryJournalStyle.Unread(label.transform, -(bodyWidth - 135) / 2 - 17, 0);
                    _unread.Add(("note", note.Id, (RectTransform)label.transform, notes.viewport));
                }
            }
            StoryJournalStyle.Expand(
                _root,
                "Show history",
                _history,
                width / 2 - 29,
                notesY + noteHeight / 2 - 27,
                () =>
                {
                    _history = !_history;
                    Render();
                }
            );
        }

        var linksHeight = selected.Links.Length > 0 ? Math.Min(160, 80 + selected.Links.Length * 32) : 0;
        var objectiveHeight = height - headerHeight - noteHeight - linksHeight;
        var objectiveY = -height / 2 + linksHeight + objectiveHeight / 2;
        var objectives = StoryJournalStyle.Scroll(
            _ui,
            _root,
            "Chapter objectives",
            bodyWidth,
            objectiveHeight,
            center,
            objectiveY,
            new RectOffset(25, 64, 40, 40)
        );
        _objectivesScroll = objectives;
        _restoreScroll.Add((objectives, objectivePosition));
        var contentWidth = bodyWidth - 106;
        foreach (var main in new[] { true, false })
        {
            var group = selected.Objectives.Where(o => o.Main == main && (_completed || !o.Complete && !o.Failed)).ToArray();
            if (group.Length == 0)
                continue;
            if (!main)
                UiElements.Rect("Group spacing", objectives.content, 0, 20).gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
            var title = _ui.Label(
                objectives.content,
                "Objective group",
                main ? "Main objectives" : "Optional objectives",
                18,
                contentWidth,
                22
            );
            title.color = StoryJournalStyle.Caption;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
            foreach (var objective in group)
                RenderObjective(objectives, objective, contentWidth);
        }
        if (!selected.Objectives.Any(o => _completed || !o.Complete && !o.Failed))
        {
            var label = _ui.Label(objectives.content, "No active objectives", "No active objectives", 18, contentWidth, 28);
            label.color = StoryJournalStyle.Caption;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
        }
        if (selected.Objectives.Any(o => o.Complete || o.Failed))
            StoryJournalStyle.Expand(
                _root,
                "Show completed objectives",
                _completed,
                width / 2 - 29,
                objectiveY + objectiveHeight / 2 - 27,
                () =>
                {
                    _completed = !_completed;
                    Render();
                }
            );
        if (linksHeight > 0)
            RenderLinks(selected, bodyWidth, linksHeight, center, height, linkPosition);
        StoryJournalStyle.Artwork(
            UiElements.Rect("Chapter title shadow", _root, bodyWidth, 70, center, height / 2 - headerHeight - 35),
            "journal-title-shadow"
        );
        StoryJournalStyle.Artwork(
            UiElements.Rect("Top objective shadow", objectives.transform, bodyWidth - 17, 70, -8.5f, objectiveHeight / 2 - 35),
            "journal-task-shadow"
        );
        var bottomShadow = UiElements.Rect(
            "Bottom objective shadow",
            objectives.transform,
            bodyWidth - 17,
            70,
            -8.5f,
            -objectiveHeight / 2 + 35
        );
        StoryJournalStyle.Artwork(bottomShadow, "journal-task-shadow");
        bottomShadow.localScale = new Vector3(1, -1, 1);
        StoryJournalStyle.Outline(_root);
    }

    private void RenderChapters(float railWidth, float height, float width)
    {
        var railRect = UiElements.Rect("Chapter rail background", _root, railWidth, height, -width / 2 + railWidth / 2, 0);
        StoryJournalStyle.Artwork(railRect, "journal-rail");
        var rail = StoryJournalStyle.Scroll(_ui, railRect, "Chapters", railWidth, height, 0, 0, new RectOffset(10, 0, 2, 2));
        _chaptersScroll = rail;
        rail.content.GetComponent<VerticalLayoutGroup>().spacing = 2;
        UiElements.Stretch(rail.viewport, 0, 12);
        foreach (var chapter in _chapters)
        {
            var state =
                chapter.Status == "Complete" ? "finished"
                : chapter.Status == "Failed" ? "failed"
                : "active";
            var chosen = chapter.Id == _selected;
            var rect = UiElements.Rect("Chapter " + chapter.Id, rail.content, 100, 100);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            var background = StoryJournalStyle.Artwork(rect, "journal-chapterback" + state + (chosen ? "selected" : "normal"), true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                _selected = chapter.Id;
                Render();
            });
            var icon = _art(chapter.Icon);
            if (icon)
            {
                var image = UiElements.Fill(UiElements.Rect("Chapter icon", rect, 100, 100), Color.white);
                image.sprite = icon;
                image.preserveAspect = true;
            }
            else
            {
                var label = _ui.Label(rect, "Chapter name fallback", chapter.Name, 16, 88, 72);
                label.color = StoryJournalStyle.Text;
                label.alignment = TextAnchor.MiddleCenter;
            }
            if (state != "active")
                StoryJournalStyle.Artwork(UiElements.Rect("Chapter status icon", rect, 100, 100), "journal-chapter" + state + "icon");
            if (chosen)
                StoryJournalStyle.Artwork(UiElements.Rect("Selected chapter marker", rect, 15, 15, 42, 0), "journal-selectedchaptermarker");
            if (chapter.Unread)
                StoryJournalStyle.Unread(rect, 33, 33);
        }
        StoryJournalStyle.Outline(railRect);
    }

    private void RenderHeader(StoryChapterView chapter, float width, float height, float x, float panelHeight)
    {
        var header = UiElements.Rect("Chapter header", _root, width, height, x, panelHeight / 2 - height / 2);
        var nameWidth = width - 152;
        var imageRect = UiElements.Rect("Chapter artwork", header, nameWidth, height, -76, 0);
        var artwork = _art(chapter.Image);
        if (artwork)
        {
            var image = UiElements.Fill(imageRect, Color.white);
            image.sprite = artwork;
            imageRect.gameObject.AddComponent<StoryTitleMask>();
        }
        var title = _ui.Label(header, "Chapter label", "CHAPTER", 18, nameWidth - 40, 22, -56, 18);
        title.color = new Color32(237, 235, 214, 128);
        var name = _ui.Label(header, "Chapter name", chapter.Name, 32, nameWidth - 40, 45, -56, -13);
        name.color = StoryJournalStyle.Text;
        var complete = chapter.Status == "Complete";
        var failed = chapter.Status == "Failed";
        var statusRect = UiElements.Rect("Chapter status panel", header, 150, 150, width / 2 - 75, 0);
        StoryJournalStyle.Artwork(
            statusRect,
            complete ? "journal-complete"
                : failed ? "journal-failed"
                : "journal-active"
        );
        var status = _ui.Label(statusRect, "Chapter status", chapter.Status.ToUpperInvariant(), 14, 140, 40);
        status.color =
            complete ? StoryJournalStyle.Complete
            : failed ? StoryJournalStyle.Failed
            : StoryJournalStyle.Active;
        status.fontStyle = FontStyle.Bold;
        status.alignment = TextAnchor.MiddleCenter;
        StoryJournalStyle.Artwork(UiElements.Rect("Title separator", header, 2, height, width / 2 - 151, 0), "journal-title-separator");
    }

    private void RenderObjective(ScrollRect scroll, StoryObjectiveView objective, float width)
    {
        var counterWidth = objective.Counter.Length > 0 ? 72 : 0;
        var textWidth = width - 34 - counterWidth;
        var textHeight = Measure(objective.Text, 20, textWidth);
        var hintHeight = objective.Hint.Length == 0 ? 0 : Measure(objective.Hint, 14, textWidth) + 2;
        var row = UiElements.Rect("Objective", scroll.content, width, textHeight + hintHeight);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = textHeight + hintHeight;
        var color =
            objective.Failed ? StoryJournalStyle.Failed
            : objective.Complete ? new Color32(139, 143, 136, 255)
            : StoryJournalStyle.Text;
        var label = _ui.Label(row, "Objective text", objective.Text, 20, textWidth, textHeight, (34 - counterWidth) / 2f, hintHeight / 2);
        label.color = color;
        label.alignment = TextAnchor.UpperLeft;
        var box = UiElements.Rect("Objective checkbox", row, 20, 20, -width / 2 + 10, (textHeight + hintHeight) / 2 - 11);
        StoryJournalStyle.Artwork(box, "journal-checkboxbackground");
        if (objective.Complete || objective.Failed)
            StoryJournalStyle
                .Artwork(UiElements.Rect("Objective result", box, 14, 14), objective.Failed ? "journal-cross" : "journal-check")
                .color = color;
        if (counterWidth > 0)
        {
            var counter = _ui.Label(
                row,
                "Objective counter",
                objective.Counter,
                20,
                counterWidth,
                textHeight,
                width / 2 - counterWidth / 2,
                hintHeight / 2
            );
            counter.color = color;
            counter.alignment = TextAnchor.UpperRight;
        }
        if (hintHeight > 0)
        {
            var hint = _ui.Label(
                row,
                "Objective hint",
                objective.Hint,
                14,
                textWidth,
                hintHeight,
                (34 - counterWidth) / 2f,
                -textHeight / 2
            );
            hint.color = StoryJournalStyle.Text;
            hint.alignment = TextAnchor.UpperLeft;
        }
        if (objective.Unread)
            _unread.Add(("condition", objective.Id, row, scroll.viewport));
    }

    private void RenderLinks(StoryChapterView chapter, float width, float height, float x, float panelHeight, float position)
    {
        var links = StoryJournalStyle.Scroll(
            _ui,
            _root,
            "Related items",
            width,
            height,
            x,
            -panelHeight / 2 + height / 2,
            new RectOffset(25, 64, 20, 20)
        );
        links.content.GetComponent<VerticalLayoutGroup>().spacing = 4;
        _linksScroll = links;
        _restoreScroll.Add((links, position));
        var title = _ui.Label(links.content, "Related items label", "Related items", 18, width - 106, 22);
        title.color = StoryJournalStyle.Caption;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
        foreach (var link in chapter.Links)
        {
            var row = UiElements.Rect("Related item row", links.content, width - 106, 28);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            var label = _ui.Label(row, "Related item", link.Name, 16, width - 140, 28, 17, 0);
            label.color = StoryJournalStyle.Text;
            var button = row.gameObject.AddComponent<Button>();
            UiElements.Fill(row, Color.clear, true);
            button.targetGraphic = label;
            button.onClick.AddListener(() => _link(link.Id));
            if (link.Kind.IndexOf("craft", StringComparison.OrdinalIgnoreCase) >= 0)
                StoryJournalStyle.Artwork(
                    UiElements.Rect("Link type", row, 14, 14, -(width - 106) / 2 + 7, 0),
                    "journal-craftquestlinkicon"
                );
            if (link.Unread)
                StoryJournalStyle.Unread(row, -(width - 106) / 2 + 17, 0);
        }
    }

    private float Measure(string text, int size, float width, FontStyle style = FontStyle.Normal)
    {
        var settings = new TextGenerationSettings
        {
            font = _ui.Font,
            fontSize = size,
            fontStyle = style,
            lineSpacing = 1,
            scaleFactor = 1,
            generationExtents = new Vector2(Math.Max(1, width), 0),
            horizontalOverflow = HorizontalWrapMode.Wrap,
            verticalOverflow = VerticalWrapMode.Overflow,
            textAnchor = TextAnchor.UpperLeft,
            color = Color.white,
        };
        return Math.Max(size + 2, Mathf.Ceil(new TextGenerator().GetPreferredHeight(text, settings)));
    }
}
