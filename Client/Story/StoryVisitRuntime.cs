using Cysharp.Threading.Tasks;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Story;
using WTT.Campaigns.UI.Media;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using ZLinq;

namespace WTT.Campaigns.Client.Story;

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
    private UniTaskCompletionSource<bool>? _continue;
    internal bool InputBlocked
    {
        get { return !_nativeWindow && (_surface != null || Time.frameCount <= _blockedThrough); }
    }

    private void Awake()
    {
        Instance = this;
        StoryClient.Changed += Render;
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
            response.Facts!.Scene = host.Native.gameObject.scene.name;
            var entries = response
                .Definition!.EntryPoints.AsValueEnumerable()
                .Where(e =>
                    e.TraderId == _trader
                    && e.Kind == "InLobby"
                    && StoryProjection.EntryAvailable(e, response.Definition, response.State!, response.Facts!)
                )
                .ToArray();
            if (entries.Length == 1)
            {
                response = await StoryClient.Mutate(
                    "start",
                    entries[0].Id,
                    scene: host.Native.gameObject.scene.name,
                    chooseItems: ChooseHandoverItems
                );
                if (generation != _generation || _surface == null)
                {
                    return;
                }

                _entrySelection = false;
                await StoryPresentationDispatcher.Dispatch(response);
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
        var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
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
        var custom = StoryMediaStore.Trader(traderId);
        var prefab = _bundle.LoadAsset<GameObject>(
            custom?.Asset ?? "assets/mods/wtt-campaigns.assets/storytraders/" + traderId + ".prefab"
        );
        if (!prefab)
        {
            throw new InvalidDataException("The trader bundle does not contain its registered room.");
        }
        if (custom != null && prefab.activeSelf)
        {
            throw new InvalidDataException("A custom trader room must have an inactive prefab root.");
        }

        _room = custom == null ? StoryRoomCamera.InstantiateRoom(prefab) : StoryRoomCamera.InstantiateCustomRoom(prefab);
        _room.SetActive(false);
        var camera = StoryRoomCamera.Prepare(_room, custom == null ? traderId : "");
        _reader = _room.GetComponentsInChildren<SequenceReader>(true).AsValueEnumerable().SingleOrDefault();
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

    internal async UniTask<bool> PresentResponse(StoryResponse response, SequenceReader? reader = null)
    {
        try
        {
            if (_surface == null)
            {
                _trader = response.State!.Conversation?.TraderId ?? "";
                CreateSurface(response.CharacterId);
                _surface!.SetBackgroundVisible(false);
                _reader = reader;
            }
            _busy = true;
            var generation = _generation;
            await Present(response);
            return _surface != null && generation == _generation;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            Clear();
            throw;
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    internal async UniTask CloseForPresentation()
    {
        await EndConversation();
        Clear();
    }

    internal void FinishPresentation(StoryResponse response)
    {
        if (response.State?.Conversation is { Closed: true })
        {
            Clear();
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
                .AsValueEnumerable()
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
        if (id == "continue" && _continue != null)
        {
            _continue.TrySetResult(true);
            return;
        }
        if (_busy || _surface == null)
        {
            return;
        }
        if (id is "trade" or "tasks" or "services")
        {
            await Navigate(
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
            if (generation != _generation || _surface == null)
            {
                return;
            }

            var response = await StoryClient.Mutate(
                id.StartsWith("entry:", StringComparison.Ordinal) ? "start" : "select",
                id.StartsWith("entry:", StringComparison.Ordinal) ? id.Substring(6) : id,
                scene: _host ? _host!.Native.gameObject.scene.name : "",
                chooseItems: ChooseHandoverItems
            );
            if (generation != _generation || _surface == null)
            {
                return;
            }

            await StoryPresentationDispatcher.Dispatch(response);
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

    private async Task<List<string>> ChooseHandoverItems(StoryHandover handover)
    {
        if (!_host || Plugin.InRaid)
        {
            throw new InvalidOperationException("Return to the trader to hand over quest items.");
        }

        var native = _host!.Native;
        var condition =
            JsonConvert.DeserializeObject<ConditionHandoverItem>(handover.ConditionJson, EftJsonConverters.Converters)
            ?? throw new InvalidDataException("The handover objective is missing.");
        var candidates = native
            .Profile.Inventory.AllRealPlayerItems.AsValueEnumerable()
            .Where(i => handover.Candidates.Contains(i.Id))
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("The selected quest items are no longer available.");
        }

        var completion = new UniTaskCompletionSource<Item[]>();
        var window = ItemUiContext.Instance.HandoverQuestItemsWindow;
        _nativeWindow = true;
        _surface!.Root.SetActive(false);
        var context = window.Show(
            condition,
            handover.Current,
            candidates,
            native.Profile,
            native.InventoryController,
            items => completion.TrySetResult(items),
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
            return (await completion.Task).AsValueEnumerable().Select(item => item.Id).Distinct().ToList();
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

    private async UniTask Present(StoryResponse response)
    {
        _entrySelection = false;
        var generation = _generation;
        if (response.Lines.Count == 0 && response.Line != null)
        {
            _text = Plugin.Localized(response.Line.Id + " text", response.Line.Text);
        }

        for (var lineIndex = 0; lineIndex < response.Lines.Count; lineIndex++)
        {
            if (_surface == null || generation != _generation)
            {
                return;
            }
            var line = response.Lines[lineIndex];
            if (!_reader && line.Playback.Animations.Count + line.Playback.SecondaryAnimations.Count + line.Playback.LipSyncs.Count > 0)
            {
                throw new InvalidDataException("This dialogue uses native animation cues but its room has no SequenceReader.");
            }

            _text = Plugin.Localized(line.Id + " text", line.Text);
            var media = _media;
            media?.Set(line.Playback);
            var mediaPlayback = media?.Wait() ?? UniTask.CompletedTask;
            try
            {
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
                            playback.Animations.AsValueEnumerable().Select(Animation).ToList(),
                            playback.SecondaryAnimations.AsValueEnumerable().Select(Animation).ToList(),
                            playback
                                .LipSyncs.AsValueEnumerable()
                                .Select(s => new LipSyncParams
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
            }
            catch
            {
                media?.Stop();
                throw;
            }
            finally
            {
                // Consume the single-use operation even if native animation setup or playback fails.
                await mediaPlayback;
            }

            if (
                StoryPlaybackRules.WaitForContinue(line, lineIndex < response.Lines.Count - 1, response.State?.Conversation?.Closed == true)
            )
            {
                var continuation = new UniTaskCompletionSource<bool>();
                _continue = continuation;
                Render();
                try
                {
                    await continuation.Task;
                }
                finally
                {
                    if (_continue == continuation)
                    {
                        _continue = null;
                    }
                }
            }
        }
        if (generation != _generation || _surface == null)
        {
            return;
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

    private async UniTask EnterRoom()
    {
        if (!_reader)
        {
            return;
        }

        var animation = _reader!
            .GetComponent<AnimationDictionary>()
            .GetKeysWithMinDurations()
            .AsValueEnumerable()
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
        var lines = response.Definition!.Dialogs.AsValueEnumerable().SelectMany(d => d.Lines).ToDictionary(l => l.Id);
        var history = response
            .State.Conversation.History.AsValueEnumerable()
            .Where(lines.ContainsKey)
            .Select(id => Plugin.Localized(id + " text", lines[id].Text))
            .JoinToString("\n\n");
        if (_continue != null)
        {
            _panel.Set(
                Plugin.Localized(_trader + " Nickname", _trader),
                _text,
                history,
                new[]
                {
                    new StoryReplyView { Id = "continue", Text = "Continue" },
                },
                false
            );
            return;
        }
        _panel.Set(
            Plugin.Localized(_trader + " Nickname", _trader),
            _text,
            history,
            response
                .Choices.AsValueEnumerable()
                .Select(l => new StoryReplyView
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

    internal async UniTask Navigate(TraderScreensGroup.ETraderMode mode)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _busy = true;
        var generation = ++_generation;
        var host = _host;
        Skip();
        await EndConversation();
        if (generation != _generation)
        {
            return;
        }

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
        {
            return;
        }

        Clear();
    }

    private static async UniTask EndConversation()
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
        _continue?.TrySetCanceled();
        _continue = null;
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
        StoryClient.Changed -= Render;
        Clear();
    }
}
