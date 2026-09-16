using Cysharp.Threading.Tasks;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using UnityEngine;
using WTT.Campaigns.Shared.Story;
using ZLinq;

namespace WTT.Campaigns.Client.Story;

public sealed class StoryRaidRuntime : MonoBehaviour
{
    internal static StoryRaidRuntime Instance = null!;
    private Player? _player;
    private bool _loading;
    private readonly List<StorySceneBinding> _bindings = new();
    private readonly HashSet<string> _pending = new();
    private readonly SemaphoreSlim _reports = new(1);
    private readonly Dictionary<string, string> _itemScenes = new();
    private readonly Dictionary<string, (Player Player, string Template)> _earlyPickups = new();
    private StoryInteractionPrompt? _prompt;
    private bool _interactionCaptured;
    private float _nextObservation;
    private bool _observing;
    private string _observationKey = "";
    private float _bindRetryAt;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        var player = StoryClient.Available && Plugin.SeasonalPlayer ? Plugin.Player : null;
        if (
            player
            && Plugin.InRaid
            && !_loading
            && !_observing
            && !Plugin.Busy
            && UnityEngine.Time.realtimeSinceStartup >= _nextObservation
        )
        {
            RefreshObservation();
        }

        var focused = player ? FocusedInteraction() : null;
        if (!_loading && !Plugin.Busy && !StoryPresentationDispatcher.Active && !StoryVisitRuntime.Instance.InputBlocked)
        {
            foreach (var pickup in _earlyPickups.AsValueEnumerable().Where(p => p.Value.Player == player).ToArray())
            {
                _earlyPickups.Remove(pickup.Key);
                Collect(pickup.Key, pickup.Value.Template);
            }
        }
        if (focused)
        {
            _prompt ??= new StoryInteractionPrompt();
        }
        _prompt?.Show(focused);
        if (player == _player || _loading || Time.realtimeSinceStartup < _bindRetryAt)
        {
            return;
        }
        Clear();
        _player = player;
        foreach (var id in _earlyPickups.AsValueEnumerable().Where(p => p.Value.Player != player).Select(p => p.Key).ToArray())
        {
            _earlyPickups.Remove(id);
        }
        if (player != null)
        {
            Bind(player);
        }
    }

    private StorySceneBinding? FocusedInteraction()
    {
        if (Plugin.Busy || Cursor.visible || !Plugin.SeasonalPlayer || Plugin.Player?.HealthController?.IsAlive != true)
        {
            return null;
        }
        var camera = Camera.main;
        if (!camera || !Physics.Raycast(camera!.transform.position, camera.transform.forward, out var hit, 2.5f))
        {
            return null;
        }
        return _bindings
            .AsValueEnumerable()
            .FirstOrDefault(b => b && b.CanInteract() && (hit.transform == b.transform || hit.transform.IsChildOf(b.transform)));
    }

    internal void ConsumeInteraction(List<ECommand> commands)
    {
        if (_interactionCaptured && commands.Remove(ECommand.EndInteracting))
        {
            _interactionCaptured = false;
        }
        if (!commands.Contains(ECommand.BeginInteracting))
        {
            return;
        }
        var focused = FocusedInteraction();
        if (!focused)
        {
            return;
        }
        commands.Remove(ECommand.BeginInteracting);
        _interactionCaptured = !commands.Remove(ECommand.EndInteracting);
        focused!.Interact();
    }

    internal void ClearCapturedInteraction()
    {
        _interactionCaptured = false;
    }

    private async void Bind(Player player)
    {
        _loading = true;
        try
        {
            var snapshot = await StoryClient.Load();
            if (!player || player != Plugin.Player || snapshot.State?.Raid is not { Finished: false } raid)
            {
                return;
            }
            var transforms = Resources
                .FindObjectsOfTypeAll<Transform>()
                .AsValueEnumerable()
                .Where(static t => t.gameObject.scene.IsValid())
                .GroupBy(ObjectPath)
                .ToDictionary(g => g.Key, g => g.AsValueEnumerable().ToArray());
            foreach (
                var loot in Resources
                    .FindObjectsOfTypeAll<EFT.Interactive.LootItem>()
                    .AsValueEnumerable()
                    .Where(l => l.gameObject.scene.IsValid())
            )
            {
                var items = loot.Item is EFT.InventoryLogic.ContainerCollection collection
                    ? collection.GetAllItemsFromCollection()
                    : new[] { loot.Item };
                foreach (var item in items.AsValueEnumerable().Where(static i => i != null))
                {
                    _itemScenes[item.Id] = loot.gameObject.scene.name;
                }
            }
            foreach (
                var container in Resources
                    .FindObjectsOfTypeAll<EFT.Interactive.LootableContainer>()
                    .AsValueEnumerable()
                    .Where(static c => c.gameObject.scene.IsValid())
            )
            {
                if (container.ItemOwner?.RootItem is ContainerCollection collection)
                {
                    foreach (var item in collection.GetAllItemsFromCollection())
                    {
                        _itemScenes[item.Id] = container.gameObject.scene.name;
                    }
                }
            }
            foreach (
                var binding in snapshot
                    .Definition!.RaidBindings.AsValueEnumerable()
                    .Where(b => b.Location == raid.Location && b.Kind != "Collectible")
            )
            {
                var authoredZone = binding.ZoneId.Length > 0 ? Spatial.ZoneRuntime.Instance?.Find(binding.ZoneId) : null;
                if (binding.ZoneId.Length > 0 && !authoredZone)
                    throw new InvalidOperationException("Story zone is not ready: " + binding.ZoneId);
                Transform[]? matches = authoredZone ? new[] { authoredZone!.transform } : null;
                if (matches == null && (!transforms.TryGetValue(binding.ObjectPath, out matches) || matches.Length != 1))
                {
                    Plugin.LogInfo("Story interaction requires one exact scene object: " + binding.ObjectPath);
                    continue;
                }
                var component = matches[0].gameObject.AddComponent<StorySceneBinding>();
                component.Initialize(binding);
                _bindings.Add(component);
            }
            foreach (var pickup in _earlyPickups.AsValueEnumerable().Where(p => p.Value.Player == player).ToArray())
            {
                _earlyPickups.Remove(pickup.Key);
                CollectReady(pickup.Key, pickup.Value.Template);
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _player = null;
            _bindRetryAt = Time.realtimeSinceStartup + 5;
        }
        finally
        {
            _loading = false;
        }
    }

    internal static string ObjectPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }
        return transform.gameObject.scene.name + ":/" + string.Join("/", names);
    }

    internal async void Report(StoryRaidBinding binding, string? kind = null, string itemId = "")
    {
        var pendingKey = binding.Id + ":" + itemId;
        if (!StoryClient.Available || !Plugin.SeasonalPlayer || !_pending.Add(pendingKey))
        {
            return;
        }
        var owner = _bindings
            .AsValueEnumerable()
            .FirstOrDefault(b =>
                b
                && (
                    binding.ZoneId.Length > 0
                        ? b.gameObject == Spatial.ZoneRuntime.Instance?.Find(binding.ZoneId)
                        : ObjectPath(b.transform) == binding.ObjectPath
                )
            );
        var character = Plugin.Player!.Profile.Id;
        var raid = StoryClient.Current?.State?.Raid?.Id;
        bool CurrentContext()
        {
            return this
                && Plugin.SeasonalPlayer
                && Plugin.Player?.Profile.Id == character
                && Plugin.Player.HealthController?.IsAlive == true
                && StoryClient.Current?.State?.Raid is { Finished: false } current
                && current.Id == raid;
        }

        await _reports.WaitAsync();
        try
        {
            while (CurrentContext() && (Plugin.Busy || StoryPresentationDispatcher.Active || StoryVisitRuntime.Instance.InputBlocked))
            {
                await UniTask.Delay(100, delayType: DelayType.Realtime);
            }
            if (!CurrentContext())
            {
                return;
            }

            var scene = owner ? owner!.gameObject.scene.name : _itemScenes.GetValueOrDefault(itemId, "");
            var response = await StoryClient.Mutate("raid", binding.Id, kind ?? binding.Kind, itemId: itemId, scene: scene);
            var reader = owner ? owner!.GetComponentInParent<EFT.AnimationSequencePlayer.SequenceReader>() : null;
            await StoryPresentationDispatcher.Dispatch(response, reader);
            if (
                binding.Kind == "Cinematic"
                && StoryClient.Current?.State?.Raid?.Cinematic.Length == 0
                && !StoryClient.Current.State.CompletedBindings.Contains(binding.Id)
                && !StoryClient.Current.State.Raid.Seen.Contains(binding.Id)
            )
            {
                owner?.RetryTrigger();
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            owner?.RetryTrigger();
        }
        finally
        {
            _pending.Remove(pendingKey);
            _reports.Release();
        }
    }

    internal void Collect(string itemId, string templateId)
    {
        if (!StoryClient.Available || !Plugin.SeasonalPlayer || !Plugin.Player)
        {
            return;
        }
        if (
            _loading
            || _player != Plugin.Player
            || Plugin.Busy
            || StoryPresentationDispatcher.Active
            || StoryVisitRuntime.Instance.InputBlocked
            || StoryClient.Current?.State?.Raid is not { Finished: false }
        )
        {
            _earlyPickups[itemId] = (Plugin.Player!, templateId);
            return;
        }
        CollectReady(itemId, templateId);
    }

    private void CollectReady(string itemId, string templateId)
    {
        if (StoryClient.Current?.State?.Raid is not { Finished: false } raid)
        {
            return;
        }
        foreach (
            var binding in StoryClient
                .Current.Definition!.RaidBindings.AsValueEnumerable()
                .Where(b => b.Location == raid.Location && b.Kind == "Collectible" && b.ItemId == templateId)
        )
        {
            if (raid.SpawnedItems.ContainsKey(itemId) && !raid.PickedItems.Contains(itemId))
            {
                Report(binding, itemId: itemId);
            }
        }
    }

    private void Clear()
    {
        foreach (var binding in _bindings.AsValueEnumerable().Where(static b => b))
        {
            Destroy(binding);
        }
        _bindings.Clear();
        _itemScenes.Clear();
        _prompt?.Dispose();
        _prompt = null;
        _interactionCaptured = false;
        _player = null;
        _observationKey = "";
    }

    private async void RefreshObservation()
    {
        _observing = true;
        _nextObservation = Time.realtimeSinceStartup + .5f;
        try
        {
            var observation = StoryRaidObserver.Capture();
            if (observation == null)
            {
                return;
            }

            observation.Sequence = 0;
            var key = Newtonsoft.Json.JsonConvert.SerializeObject(observation);
            if (key == _observationKey)
            {
                return;
            }

            await StoryClient.Load();
            _observationKey = key;
        }
        catch (Exception exception)
        {
            _nextObservation = Time.realtimeSinceStartup + 5;
            Plugin.LogInfo("Story raid refresh will retry: " + exception.Message);
        }
        finally
        {
            _observing = false;
        }
    }

    private void OnDestroy()
    {
        Clear();
    }
}
