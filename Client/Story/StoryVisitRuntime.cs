using EFT;
using EFT.AnimationSequencePlayer;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using SeasonalPerks.Client.UI;
using SeasonalPerks.Shared.Story;
using SeasonalPerks.UI.Media;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Screens;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

public sealed class StoryVisitRuntime : MonoBehaviour
{
    internal static StoryVisitRuntime Instance = null!;
    private StoryTraderHost? _host;
    private StoryPresentationSurface? _surface;
    private StoryConversationPanel? _panel;
    private AssetBundle? _bundle;
    private GameObject? _room;
    private SequenceReader? _reader;
    private StoryDialogueMedia? _media;
    private string _character = "";
    private string _trader = "";
    private string _text = "";
    private bool _busy;
    private bool _closing;
    private bool _nativeWindow;
    private bool _entrySelection;
    private bool _startedInRaid;
    private Action? _cancelSelection;
    private int _generation;
    private int _blockedThrough;
    internal bool InputBlocked
    {
        get { return !_nativeWindow && (_surface != null || Time.frameCount <= _blockedThrough); }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (_surface == null)
        {
            return;
        }
        if (!StoryClient.Available || Plugin.Current!.EffectiveProfileId != _character || !_surface.Root)
        {
            Clear();
        }
        else if (_startedInRaid != Plugin.InRaid || _startedInRaid && Plugin.Player?.HealthController?.IsAlive != true)
        {
            _ = EndConversation();
            Clear();
        }
        else if (Input.GetKeyDown(KeyCode.Escape) && !Plugin.Busy)
        {
            Close();
        }
    }

    internal async void Open(StoryTraderHost host)
    {
        if (_busy || _surface != null || Plugin.InRaid)
        {
            return;
        }
        _busy = true;
        _host = host;
        var traderId = host.Native.Trader.Id;
        var generation = ++_generation;
        try
        {
            var response = await StoryClient.Load();
            if (!host || !host.isActiveAndEnabled || generation != _generation || host.Native.Trader.Id != traderId)
            {
                return;
            }
            _host = host;
            _trader = traderId;
            CreateSurface(response.CharacterId);
            LoadRoom(_trader);
            RenderEntries(Array.Empty<StoryEntryPoint>());
            Render();
            await EnterRoom();
            if (generation != _generation || _surface == null)
            {
                return;
            }
            var entries = response
                .Definition!.EntryPoints.Where(e =>
                    e.TraderId == _trader
                    && e.Kind == "InLobby"
                    && StoryRules.Evaluate(e.Condition, response.Definition, response.State!, response.Facts!)
                )
                .ToArray();
            if (entries.Length == 1)
            {
                response = await StoryClient.Mutate("start", entries[0].Id);
                if (generation != _generation || _surface == null)
                    return;
                _entrySelection = false;
                await Present(response);
            }
            else
            {
                RenderEntries(entries);
            }
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                Clear();
            }
            Plugin.Error(exception);
        }
        finally
        {
            if (generation == _generation)
            {
                _busy = false;
                Render();
            }
        }
    }

    private void CreateSurface(string character)
    {
        if (SequencePlayer.IsPlaying)
        {
            throw new InvalidOperationException("Finish the current NPC playback before opening this conversation.");
        }
        _character = character;
        _startedInRaid = Plugin.InRaid;
        _surface = new StoryPresentationSurface("Seasonal trader visit", 32000);
        var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/seasonalperks.assets/fonts/bender.ttf");
        _media = new StoryDialogueMedia(_surface.Root.transform, font);
        _panel = new StoryConversationPanel(
            _surface.Root.transform,
            font,
            Select,
            Close,
            Skip,
            StoryUiArtwork.Load("reply"),
            SeasonUi.Instance.PlayInterfaceSound
        );
    }

    private void LoadRoom(string traderId)
    {
        _bundle = StoryMediaStore.OpenTrader(traderId);
        var prefab = _bundle.LoadAsset<GameObject>("assets/mods/seasonalperks.assets/storytraders/" + traderId + ".prefab");
        if (!prefab)
        {
            throw new InvalidDataException("The trader bundle does not contain its registered room.");
        }
        _room = StoryRoomCamera.InstantiateRoom(prefab);
        _room.SetActive(false);
        var camera = StoryRoomCamera.Prepare(_room, traderId);
        _reader = _room.GetComponentsInChildren<SequenceReader>(true).Single();
        foreach (var source in _room.GetComponentsInChildren<AudioSource>(true))
        {
            StoryAudio.Configure(source);
        }
        _surface!.UseCamera(camera);
        _room.SetActive(true);
        if (!camera.isActiveAndEnabled)
        {
            throw new InvalidDataException("The trader room camera did not activate.");
        }
        Plugin.LogInfo("Story visit camera ready: " + traderId);
    }

    internal async void ShowConversation(StoryResponse response, SequenceReader? reader = null)
    {
        try
        {
            if (_surface == null)
            {
                _trader = response.State!.Conversation!.TraderId;
                CreateSurface(response.CharacterId);
                // In-raid NPCs retain the active game camera and their own scene object.
                _surface!.SetBackgroundVisible(false);
                _reader = reader;
            }
            _busy = true;
            await Present(response);
        }
        catch (Exception exception)
        {
            Clear();
            Plugin.Error(exception);
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    private void RenderEntries(StoryEntryPoint[] entries)
    {
        _entrySelection = true;
        _text = "";
        _panel?.Set(
            Plugin.Localized(_trader + " Nickname", _trader),
            "",
            "",
            entries
                .Select(e => new StoryReplyView { Id = "entry:" + e.Id, Text = Plugin.Localized(e.DialogId + " name", "Talk") })
                .Concat(Navigation())
                .ToArray(),
            _busy
        );
    }

    private IEnumerable<StoryReplyView> Navigation()
    {
        if (_host)
        {
            yield return new StoryReplyView { Id = "trade", Text = "Want to trade?" };
            yield return new StoryReplyView { Id = "tasks", Text = "Got any jobs for me?" };
            if (_host!.Native._servicesScreen.CheckAvailableServices(_host.Native.Trader, _host.Native.Session.SessionMode))
            {
                yield return new StoryReplyView { Id = "services", Text = "Services" };
            }
        }
    }

    private async void Select(string id)
    {
        if (_busy || _surface == null)
        {
            return;
        }
        if (id is "trade" or "tasks" or "services")
        {
            Navigate(
                id == "trade" ? TraderScreensGroup.ETraderMode.Trade
                : id == "tasks" ? TraderScreensGroup.ETraderMode.Tasks
                : TraderScreensGroup.ETraderMode.Services
            );
            return;
        }
        _busy = true;
        var generation = _generation;
        Render();
        try
        {
            Skip();
            var items = await ChooseHandoverItems(id);
            if (generation != _generation || _surface == null)
                return;
            var response = await StoryClient.Mutate(
                id.StartsWith("entry:", StringComparison.Ordinal) ? "start" : "select",
                id.StartsWith("entry:", StringComparison.Ordinal) ? id.Substring(6) : id,
                itemIds: items
            );
            if (generation != _generation || _surface == null)
                return;
            await Present(response);
        }
        catch (OperationCanceledException)
        {
            // Closing the native selection window is a cancellation, not a story choice.
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _text = exception.Message;
            try
            {
                await StoryClient.Load();
            }
            catch (Exception refreshError)
            {
                Plugin.Error(refreshError);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _busy = false;
                Render();
            }
        }
    }

    private async Task<List<string>> ChooseHandoverItems(string lineId)
    {
        var selected = new List<string>();
        var response = StoryClient.Current!;
        var line = response.Choices.FirstOrDefault(l => l.Id == lineId);
        foreach (var action in line?.Actions.Where(a => a.Type == StoryActionType.HandoverItem) ?? Enumerable.Empty<StoryAction>())
        {
            if (!_host || Plugin.InRaid)
            {
                throw new InvalidOperationException("Return to the trader to hand over quest items.");
            }
            var native = _host!.Native;
            var quest = native.QuestController.Quests.Single(q => q.Id == action.QuestId);
            var condition = quest
                .Template.Conditions[EQuestStatus.AvailableForFinish]
                .OfType<ConditionItem>()
                .Single(c => c.id == action.ConditionId);
            var candidates = ConditionalController<Quest>.GetItemsForCondition(native.Profile.Inventory, condition);
            if (candidates.All(i => i.QuestItem || CurrencyUtil.IsCurrencyId(i.TemplateId)))
            {
                continue;
            }
            var completion = new TaskCompletionSource<Item[]>();
            var window = ItemUiContext.Instance.HandoverQuestItemsWindow;
            _nativeWindow = true;
            _surface!.Root.SetActive(false);
            var context = window.Show(
                condition,
                response.Facts!.ConditionCounters.GetValueOrDefault(action.ConditionId),
                candidates,
                native.Profile,
                native.InventoryController,
                items =>
                {
                    completion.TrySetResult(items);
                },
                canShowCloseButton: true
            );
            void Declined()
            {
                completion.TrySetCanceled();
            }
            context.OnDecline += Declined;
            _cancelSelection = Declined;
            try
            {
                selected.AddRange((await completion.Task).Select(item => item.Id));
            }
            finally
            {
                context.OnDecline -= Declined;
                _cancelSelection = null;
                _nativeWindow = false;
                if (_surface?.Root)
                {
                    _surface!.Root.SetActive(true);
                }
            }
        }
        return selected.Distinct().ToList();
    }

    private async Task Present(StoryResponse response)
    {
        _entrySelection = false;
        var generation = _generation;
        foreach (var line in response.Lines)
        {
            if (_surface == null || generation != _generation)
            {
                return;
            }
            _text = Plugin.Localized(line.Id + " text", line.Text);
            _media?.Set(line.Playback);
            var mediaPlayback = _media?.Wait() ?? Task.CompletedTask;
            Render();
            if (
                _reader
                && line.Playback.Animations.Count
                    + line.Playback.SecondaryAnimations.Count
                    + line.Playback.LipSyncs.Count
                    + line.Playback.Subtitles.Count
                    > 0
            )
            {
                var playback = line.Playback;
                await _reader!.Play(
                    new CombinedAnimationData(
                        playback.Animations.Select(Animation).ToList(),
                        playback.SecondaryAnimations.Select(Animation).ToList(),
                        playback
                            .LipSyncs.Select(s => new LipSyncParams
                            {
                                Key = s.Key,
                                Start = s.Start,
                                End = s.End,
                                Volume = s.Volume,
                            })
                            .ToList(),
                        new List<SubtitleParams>(),
                        new MediaData()
                    )
                );
            }
            await mediaPlayback;
        }
        if (generation != _generation || _surface == null)
        {
            return;
        }
        foreach (var action in response.Presentation)
        {
            if (
                action.Type is StoryActionType.TradingScreenAction or StoryActionType.QuestsScreenAction or StoryActionType.SelectSubService
            )
            {
                Navigate(
                    action.Type == StoryActionType.TradingScreenAction ? TraderScreensGroup.ETraderMode.Trade
                    : action.Type == StoryActionType.QuestsScreenAction ? TraderScreensGroup.ETraderMode.Tasks
                    : TraderScreensGroup.ETraderMode.Services
                );
                return;
            }
            if (action.Type == StoryActionType.StartCinematic)
            {
                await EndConversation();
                Clear();
                StoryCinematicRuntime.Instance.Play(action.Target, "");
                return;
            }
        }
        if (response.State?.Conversation is { Closed: true })
        {
            Clear();
        }
    }

    private static AnimationParams Animation(StorySequence sequence)
    {
        return new AnimationParams
        {
            Key = sequence.Key,
            Start = sequence.Start,
            End = sequence.End,
            AnimSpeed = sequence.Speed,
        };
    }

    private async Task EnterRoom()
    {
        var animation = _reader!
            .GetComponent<AnimationDictionary>()
            .GetKeysWithMinDurations()
            .FirstOrDefault(a =>
                a.key.StartsWith("Enterance", StringComparison.OrdinalIgnoreCase)
                || a.key.StartsWith("Entrance", StringComparison.OrdinalIgnoreCase)
            );
        if (animation != null)
        {
            var data = CombinedAnimationData.Default;
            data.animKeysWithParams.Add(
                new AnimationParams
                {
                    Key = animation.key,
                    Start = 0,
                    End = animation.duration,
                    AnimSpeed = 1,
                }
            );
            await _reader.Play(data);
        }
    }

    private void Render()
    {
        _panel?.SetBusy(_busy, _busy && (_reader || _media?.IsPlaying == true));
        var response = StoryClient.Current;
        if (_panel == null || _entrySelection || response?.State?.Conversation == null || response.State.Conversation.TraderId != _trader)
        {
            return;
        }
        var lines = response.Definition!.Dialogs.SelectMany(d => d.Lines).ToDictionary(l => l.Id);
        var history = string.Join(
            "\n\n",
            response.State.Conversation.History.Where(lines.ContainsKey).Select(id => Plugin.Localized(id + " text", lines[id].Text))
        );
        _panel.Set(
            Plugin.Localized(_trader + " Nickname", _trader),
            _text,
            history,
            response
                .Choices.Select(l => new StoryReplyView
                {
                    Id = l.Id,
                    Text = Plugin.Localized(l.Id + " text", l.Text),
                    Confirmation = Plugin.Localized(l.Id + " confirmation", l.Confirmation),
                })
                .Concat(Navigation())
                .ToArray(),
            _busy,
            _busy && (_reader || _media?.IsPlaying == true)
        );
    }

    private void Skip()
    {
        _media?.Stop();
        if (_reader)
        {
            SequencePlayer.StopSequence();
        }
    }

    private async void Navigate(TraderScreensGroup.ETraderMode mode)
    {
        if (_closing)
            return;
        _closing = true;
        _busy = true;
        var generation = ++_generation;
        var host = _host;
        Skip();
        await EndConversation();
        if (generation != _generation)
            return;
        Clear();
        if (host && host!.Native.isActiveAndEnabled)
        {
            host.Native.SetMode(mode);
        }
    }

    private async void Close()
    {
        if (Plugin.Busy || _closing)
        {
            return;
        }
        _busy = true;
        _closing = true;
        var generation = ++_generation;
        Skip();
        await EndConversation();
        if (generation != _generation)
            return;
        Clear();
    }

    private static async Task EndConversation()
    {
        try
        {
            if (StoryClient.Available && StoryClient.Current?.State?.Conversation is { Closed: false })
            {
                await StoryClient.Mutate("close");
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    internal void Detach(StoryTraderHost host)
    {
        if (_host == host)
        {
            Clear();
        }
    }

    private void Clear()
    {
        _cancelSelection?.Invoke();
        _cancelSelection = null;
        ++_generation;
        _blockedThrough = Time.frameCount + 1;
        Skip();
        _reader = null;
        if (_room)
        {
            _room!.SetActive(false);
            Destroy(_room);
        }
        _room = null;
        _media?.Dispose();
        _media = null;
        _surface?.Dispose();
        _surface = null;
        _panel = null;
        if (_bundle)
        {
            StoryMediaStore.Close(_bundle!);
        }
        _bundle = null;
        _host = null;
        _busy = false;
        _closing = false;
    }

    private void OnDestroy()
    {
        Clear();
    }
}
