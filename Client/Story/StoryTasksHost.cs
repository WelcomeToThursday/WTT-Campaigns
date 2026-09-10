using EFT.Quests;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Story;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using ZLinq;

namespace WTT.Campaigns.Client.Story;

public sealed class StoryTasksHost : MonoBehaviour
{
    private TasksScreen? _native;
    private RectTransform? _root;
    private StoryTaskTabs? _tabs;
    private StoryJournalPanel? _panel;
    private int _generation;
    private int _tab;
    private readonly HashSet<string> _reading = new();
    private readonly Dictionary<string, Sprite?> _images = new();
    private string _character = "";

    internal async void Open(TasksScreen native)
    {
        Clear();
        _native = native;
        _character = Plugin.Current!.EffectiveProfileId;
        var generation = ++_generation;
        try
        {
            var state = await StoryClient.Load();
            if (!Plugin.InRaid && state.Definition!.Quests.Count > 0)
            {
                state = await StoryClient.Mutate("reconcile");
            }
            if (!this || !native.isActiveAndEnabled || generation != _generation || !StoryClient.Available)
            {
                return;
            }
            var rect = (RectTransform)native._tasksPanel.transform;
            _root = StoryTaskLayout.CreatePanel(rect);
            var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
            _panel = new StoryJournalPanel(_root, font, Artwork, MarkRead, OpenLink);
            _tabs = new StoryTaskTabs(native._defaultQuestsToggleSpawner, Select);
            native._defaultQuestsToggleSpawner.gameObject.SetActive(false);
            native._dailyQuestsToggleSpawner.gameObject.SetActive(false);
            _tabs.Root.gameObject.SetActive(true);
            StoryClient.Changed += Refresh;
            Refresh();
            Select(0);
        }
        catch (Exception exception)
        {
            Clear();
            Plugin.Error(exception);
        }
    }

    private void Select(int tab)
    {
        _tab = tab;
        _tabs!.Select(tab);
        _root!.gameObject.SetActive(tab == 0);
        _native!._tasksPanel.gameObject.SetActive(tab != 0);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_root.parent);
        if (tab == 0)
        {
            Refresh();
        }
        if (tab != 0)
        {
            var ids = StoryClient.Current!.Definition!.Quests.AsValueEnumerable().Select(q => q.QuestId).ToHashSet();
            _native._tasksPanel.ShowQuests(q => tab == 2 ? q is DailyQuest : q is not DailyQuest && !ids.Contains(q.Id));
        }
    }

    private void Refresh()
    {
        if (_panel == null || StoryClient.Current?.CharacterId != _character)
        {
            return;
        }
        var response = StoryClient.Current;
        var definition = response.Definition!;
        var progress = response.State!;
        var facts = response.Facts!;
        var chapters = definition
            .Chapters.AsValueEnumerable()
            .OrderBy(c => c.Order)
            .Where(c => StoryRules.Evaluate(c.Visibility, definition, progress, facts))
            .Select(c => new StoryChapterView
            {
                Id = c.Id,
                Name = Plugin.Localized(c.Id + " name", c.Name),
                Image = c.Image,
                Icon = c.Icon,
                Status = StoryRules.ChapterComplete(c, definition, facts) ? "Complete" : "Active",
                Unread =
                    definition
                        .Notes.AsValueEnumerable()
                        .Any(n => n.ChapterId == c.Id && progress.Notes.ContainsKey(n.Id) && !progress.ReadNotes.Contains(n.Id))
                    || response
                        .Objectives.AsValueEnumerable()
                        .Any(o => o.ChapterId == c.Id && o.Visible && !progress.ReadConditions.Contains(o.Id)),
                Notes = definition
                    .Notes.AsValueEnumerable()
                    .Where(n => n.ChapterId == c.Id && progress.Notes.ContainsKey(n.Id))
                    .OrderBy(n => progress.Notes[n.Id])
                    .Select(n => new StoryNoteView
                    {
                        Id = n.Id,
                        Text = Plugin.Localized(n.Id + " text", n.Text),
                        Unread = !progress.ReadNotes.Contains(n.Id),
                    })
                    .ToArray(),
                Objectives = response
                    .Objectives.AsValueEnumerable()
                    .Where(o => o.ChapterId == c.Id && o.Visible)
                    .Select(o => new StoryObjectiveView
                    {
                        Id = o.Id,
                        Text = Plugin.Localized(o.Id, o.Text),
                        Hint = o.Hint,
                        Complete = o.Complete,
                        Failed = o.Failed,
                        Main = o.Main,
                        Unread = !progress.ReadConditions.Contains(o.Id),
                        Counter = o.Required > 1 ? o.Current + "/" + o.Required : "",
                    })
                    .ToArray(),
                Links = definition
                    .Notes.AsValueEnumerable()
                    .Where(n => n.ChapterId == c.Id && progress.Notes.ContainsKey(n.Id))
                    .SelectMany(n => n.Links)
                    .GroupBy(l => l.Id)
                    .Select(g => g.AsValueEnumerable().First())
                    .Select(l => new StoryLinkView
                    {
                        Id = l.Id,
                        Kind = l.Kind,
                        Name = Plugin.Localized(l.Target + " Name", l.Kind),
                        Unread = !progress.ReadLinks.Contains(l.Id),
                    })
                    .ToArray(),
            })
            .ToArray();
        _panel.SetState(chapters);
    }

    private async void MarkRead(string kind, string id)
    {
        if (_tab != 0 || !_reading.Add(kind + id))
        {
            return;
        }
        try
        {
            await StoryClient.Mutate("read", id, kind);
        }
        catch (Exception exception)
        {
            Plugin.LogInfo("Story read marker could not be saved: " + exception.Message);
        }
        finally
        {
            _reading.Remove(kind + id);
        }
    }

    private void OpenLink(string id)
    {
        var link = StoryClient.Current!.Definition!.Notes.AsValueEnumerable().SelectMany(n => n.Links).Single(l => l.Id == id);
        MarkRead("link", id);
        StoryLinks.Open(link);
    }

    private Sprite? Artwork(string id)
    {
        if (id.Length == 0)
        {
            return null;
        }
        if (_images.TryGetValue(id, out var sprite))
        {
            return sprite;
        }
        _images[id] = null;
        LoadImage(id, _generation);
        return null;
    }

    private async void LoadImage(string id, int generation)
    {
        try
        {
            var texture = await SeasonImageLoader.LoadAsync(SeasonImageLoader.PathFor("hub-images", id));
            if (!this || generation != _generation)
            {
                Destroy(texture);
                return;
            }
            _images[id] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            Refresh();
        }
        catch (Exception exception)
        {
            Plugin.LogInfo("Story image unavailable: " + exception.Message);
        }
    }

    private void LateUpdate()
    {
        if (_native != null && (!StoryClient.Available || Plugin.Current!.EffectiveProfileId != _character))
        {
            Clear();
        }
        if (_root && _tab == 0)
        {
            _panel?.Fit();
        }
    }

    private void OnDisable()
    {
        Clear();
    }

    internal void Clear()
    {
        _generation++;
        StoryClient.Changed -= Refresh;
        if (_native)
        {
            _native!._defaultQuestsToggleSpawner.gameObject.SetActive(true);
            _native._dailyQuestsToggleSpawner.gameObject.SetActive(true);
            _native._tasksPanel.gameObject.SetActive(true);
        }
        if (_root)
        {
            _root!.gameObject.SetActive(false);
            Destroy(_root!.gameObject);
        }
        if (_tabs != null)
        {
            _tabs.Root.gameObject.SetActive(false);
            Destroy(_tabs.Root.gameObject);
        }
        foreach (var sprite in _images.Values.AsValueEnumerable().Where(s => s))
        {
            Destroy(sprite!.texture);
            Destroy(sprite);
        }
        _images.Clear();
        _panel = null;
        _root = null;
        _tabs = null;
        _native = null;
    }
}
